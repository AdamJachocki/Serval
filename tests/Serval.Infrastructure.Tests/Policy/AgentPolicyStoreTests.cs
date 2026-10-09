using Microsoft.Data.Sqlite;
using Serval.Application.Authorization;
using Serval.Infrastructure.Policy;
using Xunit;

namespace Serval.Infrastructure.Tests.Policy;

public sealed class AgentPolicyStoreTests
{
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
