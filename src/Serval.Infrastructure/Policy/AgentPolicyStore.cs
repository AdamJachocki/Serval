using Microsoft.Data.Sqlite;
using Serval.Application.Authorization;
using Serval.Domain.Services;

namespace Serval.Infrastructure.Policy;

/// <summary>Agent-owned durable grant and audit state. Callers enforce identity and policy.</summary>
public sealed class AgentPolicyStore : IAgentPolicyStore
{
    public const string DatabasePath = "/var/lib/serval/policy.db";
    private const int SchemaVersion = 1;
    private readonly string path;

    public AgentPolicyStore() : this(DatabasePath) { }

    internal AgentPolicyStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public void Initialize()
    {
        using var connection = Open(create: true);
        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        var current = Convert.ToInt32(version.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (current == SchemaVersion)
        {
            return;
        }

        if (current != 0)
        {
            throw new InvalidOperationException("Unsupported Agent policy schema.");
        }

        using var transaction = connection.BeginTransaction();
        using var create = connection.CreateCommand();
        create.Transaction = transaction;
        create.CommandText = """
            CREATE TABLE grants (
                id INTEGER PRIMARY KEY,
                subject_kind TEXT NOT NULL CHECK(subject_kind IN ('User','Group')),
                subject_name TEXT NOT NULL,
                subject_id INTEGER NOT NULL,
                operation TEXT NOT NULL CHECK(operation = 'Service.View'),
                service TEXT NOT NULL,
                UNIQUE(subject_kind, subject_name, subject_id, operation, service)
            );
            CREATE TABLE audit (
                id INTEGER PRIMARY KEY,
                actor_name TEXT,
                actor_uid INTEGER,
                operation TEXT NOT NULL,
                service TEXT,
                outcome TEXT NOT NULL,
                correlation_id TEXT NOT NULL,
                timestamp_utc TEXT NOT NULL
            );
            PRAGMA user_version = 1;
            """;
        create.ExecuteNonQuery();
        transaction.Commit();
    }

    public IReadOnlyList<StoredGrant> ListGrants()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, subject_kind, subject_name, subject_id, operation, service
            FROM grants ORDER BY id
            """;
        using var reader = command.ExecuteReader();
        var grants = new List<StoredGrant>();
        while (reader.Read())
        {
            var grant = new StoredGrant(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                checked((uint)reader.GetInt64(3)), reader.GetString(4), reader.GetString(5));
            ValidateGrant(grant);
            grants.Add(grant);
        }

        return grants.AsReadOnly();
    }

    public long AddGrant(StoredGrant grant, AuditEntry audit)
    {
        ValidateGrant(grant);
        ValidateAudit(audit);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO grants(subject_kind, subject_name, subject_id, operation, service)
            VALUES($kind, $name, $id, $operation, $service);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$kind", grant.SubjectKind);
        command.Parameters.AddWithValue("$name", grant.SubjectName);
        command.Parameters.AddWithValue("$id", grant.SubjectId);
        command.Parameters.AddWithValue("$operation", grant.Operation);
        command.Parameters.AddWithValue("$service", grant.Service);
        var id = (long)command.ExecuteScalar()!;
        InsertAudit(connection, transaction, audit);
        transaction.Commit();
        return id;
    }

    public bool RemoveGrant(long id, AuditEntry audit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        ValidateAudit(audit);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT service FROM grants WHERE id = $id";
        lookup.Parameters.AddWithValue("$id", id);
        var storedService = lookup.ExecuteScalar();
        var service = storedService switch
        {
            null => null,
            string value => value,
            _ => throw new InvalidOperationException("Stored grant is invalid."),
        };
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM grants WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        var removed = command.ExecuteNonQuery() == 1;
        var removalAudit = audit with
        {
            Service = service,
            Outcome = removed ? "Success" : "NotFound",
        };
        ValidateAudit(removalAudit);
        InsertAudit(connection, transaction, removalAudit);
        transaction.Commit();
        return removed;
    }

    public void AppendAudit(AuditEntry audit)
    {
        ValidateAudit(audit);
        using var connection = Open();
        InsertAudit(connection, null, audit);
    }

    private SqliteConnection Open(bool create = false)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 1000";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void InsertAudit(SqliteConnection connection, SqliteTransaction? transaction, AuditEntry audit)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO audit(actor_name, actor_uid, operation, service, outcome, correlation_id, timestamp_utc)
            VALUES($actor, $uid, $operation, $service, $outcome, $correlation, $timestamp)
            """;
        command.Parameters.AddWithValue("$actor", (object?)audit.ActorName ?? DBNull.Value);
        command.Parameters.AddWithValue("$uid", (object?)audit.ActorUid ?? DBNull.Value);
        command.Parameters.AddWithValue("$operation", audit.Operation);
        command.Parameters.AddWithValue("$service", (object?)audit.Service ?? DBNull.Value);
        command.Parameters.AddWithValue("$outcome", audit.Outcome);
        command.Parameters.AddWithValue("$correlation", audit.CorrelationId);
        command.Parameters.AddWithValue("$timestamp", audit.Timestamp.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void ValidateGrant(StoredGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (grant.SubjectKind is not ("User" or "Group") ||
            grant.SubjectName.Length is < 1 or > 128 ||
            grant.SubjectName.Any(value => value is '/' or '\0' || char.IsControl(value)) ||
            grant.Operation != "Service.View" ||
            grant.Service.Length is < 1 or > 255 ||
            grant.Service.EndsWith("@.service", StringComparison.Ordinal))
        {
            throw new ArgumentException("Grant is invalid.", nameof(grant));
        }

        _ = new SystemServiceId(grant.Service);
    }

    private static void ValidateAudit(AuditEntry audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (audit.ActorName?.Length > 128 ||
            audit.ActorName?.Any(value => value is '/' or '\0' || char.IsControl(value)) == true ||
            audit.Operation is not ("Login" or "Logout" or "ListServices" or
                "InspectService" or "ListGrants" or "AddGrant" or "RemoveGrant") ||
            audit.Service?.Length > 255 || audit.Outcome.Length is < 1 or > 32 ||
            audit.Outcome is not ("Success" or "Denied" or "NotFound" or
                "Unauthenticated" or "InvalidRequest" or "Unavailable") ||
            audit.CorrelationId.Length is < 1 or > 64 ||
            !audit.CorrelationId.All(value => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_'))
        {
            throw new ArgumentException("Audit metadata is invalid.", nameof(audit));
        }

        if (audit.Service is not null)
        {
            _ = new SystemServiceId(audit.Service);
        }
    }
}
