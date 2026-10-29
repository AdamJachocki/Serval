using Microsoft.Data.Sqlite;
using Serval.Application.Authorization;
using Serval.Infrastructure.Policy;
using Xunit;

namespace Serval.Infrastructure.Tests.Policy;

public sealed class AgentPolicyStoreTests
{
    private static readonly string[] SupportedOperations = ["Service.View", "Environment.View", "Environment.Reveal"];
    [Fact]
    public void EnvironmentGrantsAreIndependentAndAuditNamesAreValidated()
    {
        InTemporaryStore((store, path) =>
        {
            foreach (var operation in SupportedOperations)
                store.AddGrant(Grant() with { Operation = operation }, Audit("AddGrant"));
            Assert.Equal(3, store.ListGrants().Count);
            store.AppendAudit(Audit("RevealEnvironmentVariable") with { VariableName = "CONFIG_1" });
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT variable_name FROM audit WHERE operation = 'RevealEnvironmentVariable'";
            Assert.Equal("CONFIG_1", command.ExecuteScalar());
            foreach (var invalid in new[] { "", "../FILE", "1NAME", "A\nB", new string('A', 4097) })
                Assert.Throws<ArgumentException>(() => store.AppendAudit(
                    Audit("RevealEnvironmentVariable") with { VariableName = invalid }));
            Assert.Throws<ArgumentException>(() => store.AddGrant(
                Grant() with { Operation = "Environment.Export" }, Audit("AddGrant")));
            store.Initialize();
            Assert.Equal(3, store.ListGrants().Count);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VersionOneMigrationPreservesHistoryAndRollsBackOnFailure(bool injectFailure)
    {
        InTemporaryStore((store, path) =>
        {
            using (var connection = Open(path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    DROP TABLE grants;
                    DROP TABLE audit;
                    CREATE TABLE grants (
                        id INTEGER PRIMARY KEY,
                        subject_kind TEXT NOT NULL CHECK(subject_kind IN ('User','Group')),
                        subject_name TEXT NOT NULL, subject_id INTEGER NOT NULL,
                        operation TEXT NOT NULL CHECK(operation = 'Service.View'), service TEXT NOT NULL,
                        UNIQUE(subject_kind, subject_name, subject_id, operation, service));
                    CREATE TABLE audit (id INTEGER PRIMARY KEY, actor_name TEXT, actor_uid INTEGER,
                        operation TEXT NOT NULL, service TEXT, outcome TEXT NOT NULL,
                        correlation_id TEXT NOT NULL, timestamp_utc TEXT NOT NULL);
                    INSERT INTO grants VALUES(42,'User','alice',1001,'Service.View','ordinary.service');
                    INSERT INTO audit VALUES(71,'admin',1000,'AddGrant','ordinary.service','Success','old','2026-01-01T00:00:00Z');
                    PRAGMA user_version = 1;
                    """;
                command.ExecuteNonQuery();
                if (injectFailure)
                {
                    // Force failure after the grants table has been rebuilt.
                    command.CommandText = "ALTER TABLE audit ADD COLUMN variable_name TEXT";
                    command.ExecuteNonQuery();
                }
            }
            if (injectFailure)
                Assert.Throws<SqliteException>(store.Initialize);
            else
            {
                store.Initialize();
                store.Initialize();
            }
            Assert.Equal(42, Assert.Single(store.ListGrants()).Id);
            using var verify = Open(path);
            using var query = verify.CreateCommand();
            query.CommandText = "PRAGMA user_version";
            Assert.Equal(injectFailure ? 1L : 2L, query.ExecuteScalar());
            query.CommandText = "SELECT COUNT(*) FROM audit WHERE id=71 AND correlation_id='old'";
            Assert.Equal(1L, query.ExecuteScalar());
            if (injectFailure)
            {
                query.CommandText = "INSERT INTO grants VALUES(43,'User','alice',1001,'Environment.View','ordinary.service')";
                Assert.Throws<SqliteException>(() => query.ExecuteNonQuery());
                query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='grants_v2'";
                Assert.Equal(0L, query.ExecuteScalar());
            }
            else
                store.AddGrant(Grant() with { Operation = "Environment.View" }, Audit("AddGrant"));
        });
    }

    [Fact]
    public void EmptyStoreInitializesAndGrantMutationIsAudited()
    {
        InTemporaryStore((store, path) =>
        {
            Assert.Empty(store.ListGrants());

            var id = store.AddGrant(Grant(), Audit("AddGrant"));
            var saved = Assert.Single(store.ListGrants());
            Assert.Equal(id, saved.Id);
            Assert.Equal("alice", saved.SubjectName);
            Assert.True(store.RemoveGrant(id, Audit("RemoveGrant")));
            Assert.Empty(store.ListGrants());

            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM audit";
            Assert.Equal(2L, command.ExecuteScalar());
            command.CommandText = "SELECT service FROM audit WHERE operation = 'RemoveGrant'";
            Assert.Equal("ordinary.service", command.ExecuteScalar());
        });
    }

    [Fact]
    public void FailedAuditInsertionRollsBackGrantMutation()
    {
        InTemporaryStore((store, path) =>
        {
            using (var connection = Open(path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TRIGGER reject_audit BEFORE INSERT ON audit
                    BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;
                    """;
                command.ExecuteNonQuery();
            }

            Assert.Throws<SqliteException>(() => store.AddGrant(Grant(), Audit("AddGrant")));
            Assert.Empty(store.ListGrants());
        });
    }

    [Fact]
    public void UnsupportedSchemaFailsWithoutMigration()
    {
        InTemporaryStore((store, path) =>
        {
            using (var connection = Open(path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version = 99";
                command.ExecuteNonQuery();
            }

            Assert.Throws<InvalidOperationException>(store.Initialize);
        });
    }

    private static StoredGrant Grant() =>
        new(0, "User", "alice", 1001, "Service.View", "ordinary.service");

    private static AuditEntry Audit(string operation) =>
        new("admin", 1000, operation, "ordinary.service", "Success", "test-correlation", DateTimeOffset.UtcNow);

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        return connection;
    }

    private static void InTemporaryStore(Action<AgentPolicyStore, string> assertion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "serval-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "policy.db");
            var store = new AgentPolicyStore(path);
            store.Initialize();
            assertion(store, path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
