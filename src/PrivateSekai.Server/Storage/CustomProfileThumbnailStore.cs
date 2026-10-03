using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace PrivateSekai.Storage;

public sealed class CustomProfileThumbnailStore : IDisposable
{
    private readonly object gate = new();
    private readonly SqliteConnection connection;

    public CustomProfileThumbnailStore() : this(":memory:") { }

    public CustomProfileThumbnailStore(string path)
    {
        if (path != ":memory:")
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=FULL;
                CREATE TABLE IF NOT EXISTS custom_profile_thumbnails(
                    path TEXT PRIMARY KEY, content_type TEXT NOT NULL, payload BLOB NOT NULL);
                """;
            command.ExecuteNonQuery();
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public string SaveThumbnail(string? thumbnail, string? existingThumbnailPath = null)
    {
        if (string.IsNullOrWhiteSpace(thumbnail))
            return existingThumbnailPath ?? "";

        var trimmed = StripDataUrlPrefix(thumbnail.Trim());
        if (IsThumbnailPath(trimmed))
            return trimmed;

        if (!TryDecodeBase64(trimmed, out var bytes) || bytes.Length == 0)
            return existingThumbnailPath ?? thumbnail;

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (TryReuseExistingPath(existingThumbnailPath, hash, out var reusedPath))
            return reusedPath;

        var thumbnailPath = $"{hash}/{Guid.NewGuid()}";
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO custom_profile_thumbnails VALUES($path, $type, $payload)";
            command.Parameters.AddWithValue("$path", thumbnailPath);
            command.Parameters.AddWithValue("$type", DetectContentType(bytes));
            command.Parameters.AddWithValue("$payload", bytes);
            command.ExecuteNonQuery();
        }

        return thumbnailPath;
    }

    public bool TryGetThumbnail(
        string hash,
        string thumbnailId,
        out byte[] bytes,
        out string contentType)
    {
        bytes = [];
        contentType = "application/octet-stream";

        if (!IsValidHash(hash) || !Guid.TryParse(thumbnailId, out _))
            return false;

        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload, content_type FROM custom_profile_thumbnails WHERE path=$path";
            command.Parameters.AddWithValue("$path", $"{hash}/{thumbnailId}");
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return false;
            bytes = (byte[])reader.GetValue(0);
            contentType = reader.GetString(1);
            return true;
        }
    }

    private bool TryReuseExistingPath(string? existingThumbnailPath, string hash, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(existingThumbnailPath) || !IsThumbnailPath(existingThumbnailPath))
            return false;

        var parts = existingThumbnailPath.Split('/', 2);
        if (!string.Equals(parts[0], hash, StringComparison.OrdinalIgnoreCase))
            return false;

        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM custom_profile_thumbnails WHERE path=$path";
            command.Parameters.AddWithValue("$path", existingThumbnailPath);
            if (command.ExecuteScalar() == null) return false;
        }

        path = existingThumbnailPath;
        return true;
    }

    private static bool IsThumbnailPath(string value)
    {
        var parts = value.Split('/', 2);
        return parts.Length == 2 && IsValidHash(parts[0]) && Guid.TryParse(parts[1], out _);
    }

    private static bool IsValidHash(string hash) =>
        hash.Length == 64 && hash.All(IsHexChar);

    private static bool IsHexChar(char c) =>
        c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static string StripDataUrlPrefix(string value)
    {
        var commaIndex = value.IndexOf(',');
        return value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0
            ? value[(commaIndex + 1)..]
            : value;
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static string DetectContentType(byte[] bytes)
    {
        var header = bytes.AsSpan();

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return "image/jpeg";

        if (header.Length >= 8 &&
            header[0] == 0x89 &&
            header[1] == 0x50 &&
            header[2] == 0x4E &&
            header[3] == 0x47 &&
            header[4] == 0x0D &&
            header[5] == 0x0A &&
            header[6] == 0x1A &&
            header[7] == 0x0A)
        {
            return "image/png";
        }

        if (header.Length >= 12 &&
            header[0] == 'R' &&
            header[1] == 'I' &&
            header[2] == 'F' &&
            header[3] == 'F' &&
            header[8] == 'W' &&
            header[9] == 'E' &&
            header[10] == 'B' &&
            header[11] == 'P')
        {
            return "image/webp";
        }

        return "application/octet-stream";
    }

    public void Dispose()
    {
        lock (gate) connection.Dispose();
    }
}
