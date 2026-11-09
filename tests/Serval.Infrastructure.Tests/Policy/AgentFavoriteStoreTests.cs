using Microsoft.Data.Sqlite;
using Serval.Application.Authorization;
using Serval.Infrastructure.Policy;
using Xunit;

namespace Serval.Infrastructure.Tests.Policy;

public sealed class AgentFavoriteStoreTests
{
    [Fact]
    public void FreshDatabaseIsAtSchemaThreeWithFavorites()
    {
        InTemporaryStore((store, path) =>
        {
            Assert.Equal(3L, Scalar(path, "PRAGMA user_version"));
            Assert.Equal(1L, Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'favorites'"));
            Assert.Empty(store.ListFavorites("alice", 1001));
            store.Initialize();
            Assert.Equal(3L, Scalar(path, "PRAGMA user_version"));
        });
    }

    [Fact]
    public void VersionTwoMigrationAddsFavoritesAndKeepsGrantsAndAudit()
    {
        InTemporaryStore((store, path) =>
        {
            store.AddGrant(new StoredGrant(0, "User", "alice", 1001, "Service.View", "ordinary.service"),
                Audit("AddGrant"));
            Execute(path, "DROP TABLE favorites; PRAGMA user_version = 2");

            store.Initialize();

            Assert.Equal(3L, Scalar(path, "PRAGMA user_version"));
            Assert.Single(store.ListGrants());
            Assert.Equal(1L, Scalar(path, "SELECT COUNT(*) FROM audit WHERE operation = 'AddGrant'"));
            Assert.Empty(store.ListFavorites("alice", 1001));
            Assert.Equal(FavoriteAddOutcome.Added,
                store.TryAdd("alice", 1001, "ordinary.service", 100, Audit("AddFavorite")));
        });
    }

    [Fact]
    public void VersionOneMigrationReachesVersionThreeWithFavorites()
    {
        InTemporaryStore((store, path) =>
        {
            Execute(path, """
                DROP TABLE favorites;
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
                """);

            store.Initialize();

            Assert.Equal(3L, Scalar(path, "PRAGMA user_version"));
            Assert.Equal(42, Assert.Single(store.ListGrants()).Id);
            Assert.Equal(1L, Scalar(path, "SELECT COUNT(*) FROM audit WHERE id = 71"));
            Assert.Equal(FavoriteAddOutcome.Added,
                store.TryAdd("alice", 1001, "ordinary.service", 100, Audit("AddFavorite")));
        });
    }

    [Fact]
    public void FavoritesAreIsolatedByNameAndUid()
    {
        InTemporaryStore((store, _) =>
        {
            store.TryAdd("alice", 1001, "nginx.service", 100, Audit("AddFavorite"));

            Assert.Equal(["nginx.service"], store.ListFavorites("alice", 1001));
            Assert.Empty(store.ListFavorites("alice", 2002));
            Assert.Empty(store.ListFavorites("mallory", 1001));
            Assert.Empty(store.ListFavorites("bob", 1002));
        });
    }

    [Fact]
    public void AddIsIdempotentAndListedInOrdinalOrder()
    {
        InTemporaryStore((store, _) =>
        {
            Assert.Equal(FavoriteAddOutcome.Added,
                store.TryAdd("alice", 1001, "b.service", 100, Audit("AddFavorite")));
            Assert.Equal(FavoriteAddOutcome.AlreadyPresent,
                store.TryAdd("alice", 1001, "b.service", 100, Audit("AddFavorite")));
            store.TryAdd("alice", 1001, "a.service", 100, Audit("AddFavorite"));

            Assert.Equal(["a.service", "b.service"], store.ListFavorites("alice", 1001));
        });
    }

    [Fact]
    public void LimitRejectsWithoutChangeButReAddAtLimitSucceeds()
    {
        InTemporaryStore((store, path) =>
        {
            for (var index = 0; index < 3; index++)
            {
                store.TryAdd("alice", 1001, $"s{index}.service", 3, Audit("AddFavorite"));
            }

            Assert.Equal(FavoriteAddOutcome.AlreadyPresent,
                store.TryAdd("alice", 1001, "s1.service", 3, Audit("AddFavorite")));
            Assert.Equal(FavoriteAddOutcome.LimitReached,
                store.TryAdd("alice", 1001, "extra.service", 3, Audit("AddFavorite")));
            Assert.Equal(3, store.ListFavorites("alice", 1001).Count);
            Assert.DoesNotContain("extra.service", store.ListFavorites("alice", 1001));
            Assert.Equal(FavoriteAddOutcome.Added,
                store.TryAdd("bob", 1002, "extra.service", 3, Audit("AddFavorite")));
            Assert.Equal(1L, Scalar(path, "SELECT COUNT(*) FROM audit WHERE outcome = 'LimitReached'"));
        });
    }

    [Fact]
    public void RemoveAffectsOnlyTheCaller()
    {
        InTemporaryStore((store, path) =>
        {
            store.TryAdd("alice", 1001, "nginx.service", 100, Audit("AddFavorite"));
            store.TryAdd("bob", 1002, "nginx.service", 100, Audit("AddFavorite"));
            store.TryAdd("alice", 2002, "nginx.service", 100, Audit("AddFavorite"));

            store.Remove("alice", 1001, "nginx.service", Audit("RemoveFavorite"));
            store.Remove("alice", 1001, "absent.service", Audit("RemoveFavorite"));

            Assert.Empty(store.ListFavorites("alice", 1001));
            Assert.Equal(["nginx.service"], store.ListFavorites("bob", 1002));
            Assert.Equal(["nginx.service"], store.ListFavorites("alice", 2002));
            Assert.Equal(2L, Scalar(path, "SELECT COUNT(*) FROM audit WHERE operation = 'RemoveFavorite'"));
        });
    }

    [Fact]
    public void AuditRecordsOperationServiceAndOutcome()
    {
        InTemporaryStore((store, path) =>
        {
            store.TryAdd("alice", 1001, "nginx.service", 100, Audit("AddFavorite", "nginx.service"));
            store.Remove("alice", 1001, "gone.service", Audit("RemoveFavorite", null));

            Assert.Equal("nginx.service", Scalar(path,
                "SELECT service FROM audit WHERE operation = 'AddFavorite' AND outcome = 'Success'"));
            Assert.Equal(DBNull.Value, Scalar(path,
                "SELECT service FROM audit WHERE operation = 'RemoveFavorite'"));
        });
    }

    [Fact]
    public void FailedAuditInsertRollsBackAddAndRemove()
    {
        InTemporaryStore((store, path) =>
        {
            store.TryAdd("alice", 1001, "kept.service", 100, Audit("AddFavorite"));
            Execute(path, """
                CREATE TRIGGER reject_audit BEFORE INSERT ON audit
                BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;
                """);

            Assert.Throws<SqliteException>(() =>
                store.TryAdd("alice", 1001, "new.service", 100, Audit("AddFavorite")));
            Assert.Throws<SqliteException>(() =>
                store.Remove("alice", 1001, "kept.service", Audit("RemoveFavorite")));

            Assert.Equal(["kept.service"], store.ListFavorites("alice", 1001));
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("../etc.service")]
    [InlineData("template@.service")]
    [InlineData("bad name.service")]
    public void InvalidServiceIsRejectedBeforeAnyWrite(string service)
    {
        InTemporaryStore((store, path) =>
        {
            Assert.Throws<ArgumentException>(() =>
                store.TryAdd("alice", 1001, service, 100, Audit("AddFavorite")));
            Assert.Throws<ArgumentException>(() =>
                store.Remove("alice", 1001, service, Audit("RemoveFavorite")));
            Assert.Equal(0L, Scalar(path, "SELECT COUNT(*) FROM audit"));
        });
    }

    [Fact]
    public void InvalidOwnerAndLimitAreRejected()
    {
        InTemporaryStore((store, _) =>
        {
            Assert.Throws<ArgumentException>(() => store.ListFavorites("", 1001));
            Assert.Throws<ArgumentException>(() =>
                store.TryAdd("a/b", 1001, "nginx.service", 100, Audit("AddFavorite")));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                store.TryAdd("alice", 1001, "nginx.service", 0, Audit("AddFavorite")));
        });
    }

    private static AuditEntry Audit(string operation, string? service = "nginx.service") =>
        new("alice", 1001, operation, service, "Success", "test-correlation", DateTimeOffset.UtcNow);

    private static void Execute(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        return connection;
    }

    private static void InTemporaryStore(Action<AgentPolicyStore, string> assertion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "serval-favorites-" + Guid.NewGuid().ToString("N"));
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
