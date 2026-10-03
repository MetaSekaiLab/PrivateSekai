using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Storage;

public sealed class SqliteUserStore : IUserStore, IDisposable
{
    private sealed record Snapshot(UserState State, long Revision);
    private sealed record ReadVersion(long UserId, long Revision);
    private readonly object gate = new();
    private readonly SqliteConnection connection;
    private readonly Dictionary<long, Snapshot> cache = new();
    private readonly ConditionalWeakTable<UserState, ReadVersion> versions = new();

    public SqliteUserStore(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            using var version = Command("PRAGMA user_version");
            var current = Convert.ToInt32(version.ExecuteScalar());
            if (current is not (0 or 1)) throw new NotSupportedException("Unsupported user database version.");
            using var settings = Command("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;");
            settings.ExecuteNonQuery();
            using var transaction = connection.BeginTransaction();
            using var schema = Command("""
                CREATE TABLE IF NOT EXISTS users(user_id INTEGER PRIMARY KEY, revision INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS user_blocks(
                    user_id INTEGER NOT NULL REFERENCES users(user_id),
                    name TEXT NOT NULL, format_version INTEGER NOT NULL,
                    revision INTEGER NOT NULL, payload TEXT NOT NULL,
                    PRIMARY KEY(user_id, name));
                CREATE TABLE IF NOT EXISTS user_sequence(id INTEGER PRIMARY KEY CHECK(id=1), last_id INTEGER NOT NULL);
                INSERT OR IGNORE INTO user_sequence VALUES(1, 0);
                PRAGMA user_version=1;
                """, transaction);
            schema.ExecuteNonQuery();
            transaction.Commit();
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public long ReserveId()
    {
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            using var select = Command("SELECT last_id FROM user_sequence WHERE id=1", transaction);
            var id = checked(Convert.ToInt64(select.ExecuteScalar()) + 1);
            using var update = Command("UPDATE user_sequence SET last_id=$id WHERE id=1", transaction);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
            transaction.Commit();
            return id;
        }
    }

    public long[] GetUserIds()
    {
        lock (gate)
        {
            using var command = Command("SELECT user_id FROM users ORDER BY user_id");
            using var reader = command.ExecuteReader();
            var ids = new List<long>();
            while (reader.Read()) ids.Add(reader.GetInt64(0));
            return ids.ToArray();
        }
    }

    public UserState? Read(long userId)
    {
        lock (gate)
        {
            using var transaction = connection.BeginTransaction(deferred: true);
            var saved = Load(userId, transaction);
            if (saved == null) return null;
            var copy = saved.State.DeepClone();
            versions.Add(copy, new(userId, saved.Revision));
            return copy;
        }
    }

    public void Save(long userId, UserState state)
    {
        if (state.Data.userRegistration?.userId != userId)
            throw new InvalidOperationException("User identity does not match the store key.");
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            var saved = Load(userId, transaction);
            if (saved != null && (!versions.TryGetValue(state, out var expected) ||
                expected.UserId != userId || expected.Revision != saved.Revision))
                throw new InvalidOperationException("User snapshot is stale; read it again before saving.");

            var next = new UserState();
            var changed = new List<(string Name, string Json)>();
            foreach (var block in UserBlocks.All)
            {
                var value = block.Get(state);
                var prior = saved == null ? null : block.Get(saved.State);
                if (saved != null && UserBlocks.Equal(value, prior))
                    block.Set(next, prior);
                else
                {
                    var json = JsonSerializer.Serialize(value, block.Type, UserBlocks.Json);
                    block.Set(next, JsonSerializer.Deserialize(json, block.Type, UserBlocks.Json));
                    changed.Add((block.Name, json));
                }
            }
            if (saved != null && changed.Count == 0) return;
            var revision = checked((saved?.Revision ?? 0) + 1);
            using (var user = Command("""
                INSERT INTO users VALUES($id, $revision)
                ON CONFLICT(user_id) DO UPDATE SET revision=excluded.revision;
                UPDATE user_sequence SET last_id=MAX(last_id, $id) WHERE id=1;
                """, transaction))
            {
                user.Parameters.AddWithValue("$id", userId);
                user.Parameters.AddWithValue("$revision", revision);
                user.ExecuteNonQuery();
            }
            foreach (var block in changed)
            {
                using var update = Command("""
                    INSERT INTO user_blocks VALUES($id, $name, 1, $revision, $payload)
                    ON CONFLICT(user_id,name) DO UPDATE SET payload=excluded.payload, revision=excluded.revision;
                    """, transaction);
                update.Parameters.AddWithValue("$id", userId);
                update.Parameters.AddWithValue("$name", block.Name);
                update.Parameters.AddWithValue("$revision", revision);
                update.Parameters.AddWithValue("$payload", block.Json);
                update.ExecuteNonQuery();
            }
            transaction.Commit();
            cache[userId] = new(next, revision);
            versions.Remove(state);
            versions.Add(state, new(userId, revision));
        }
    }

    private Snapshot? Load(long userId, SqliteTransaction transaction)
    {
        using var find = Command("SELECT revision FROM users WHERE user_id=$id", transaction);
        find.Parameters.AddWithValue("$id", userId);
        var result = find.ExecuteScalar();
        if (result == null) return null;
        var revision = Convert.ToInt64(result);
        if (cache.TryGetValue(userId, out var cached) && cached.Revision == revision) return cached;

        var state = new UserState();
        using var command = Command("SELECT name, format_version, payload FROM user_blocks WHERE user_id=$id", transaction);
        command.Parameters.AddWithValue("$id", userId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var block = UserBlocks.All.SingleOrDefault(b => b.Name == reader.GetString(0));
            if (block == null || reader.GetInt32(1) != 1)
                throw new NotSupportedException("Unsupported user block version or name.");
            block.Set(state, JsonSerializer.Deserialize(reader.GetString(2), block.Type, UserBlocks.Json));
        }
        if (state.Data.userRegistration?.userId != userId)
            throw new InvalidDataException("Stored user identity is invalid.");
        var snapshot = new Snapshot(state, revision);
        cache[userId] = snapshot;
        return snapshot;
    }

    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    public void Dispose()
    {
        lock (gate) connection.Dispose();
    }
}
