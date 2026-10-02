using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PrivateSekai.Client;

public static class ThumbnailDownload
{
    private const int MaximumBytes = 10 * 1024 * 1024;

    public static string ValidatePath(string path)
    {
        // 名片响应只含 hash/thumbnailId，书签响应包含完整相对路由。
        if (path.Split('/').Length == 2) path = "image/custom-profile-card/thumbnail/" + path;
        var parts = path.Split('/');
        if (parts.Length != 5 || parts[0] != "image" || parts[2] != "thumbnail" ||
            parts[1] is not ("bookmark-story" or "custom-profile-card") || path.Length > 2048 ||
            parts.Any(p => p.Length == 0 || p is "." or ".." || p.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))))
            throw new InvalidOperationException("响应图片路径不符合已支持的缩略图路由。");
        return path;
    }

    public static async Task<JsonObject> Save(HttpResponseMessage response, string outputStem, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new ClientFailure($"图片下载返回 HTTP {(int)response.StatusCode}，不自动重试。");
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var extension = contentType switch { "image/png" => ".png", "image/jpeg" => ".jpg", _ => throw new InvalidOperationException("图片响应类型未支持。") };
        if (response.Content.Headers.ContentLength > MaximumBytes)
            throw new InvalidOperationException("图片超过 10 MiB 下载上限。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (bytes.Length + read > MaximumBytes) throw new InvalidOperationException("图片超过 10 MiB 下载上限。");
            bytes.Write(buffer, 0, read);
        }
        var data = bytes.ToArray();
        byte[] signature = extension == ".png" ? [137, 80, 78, 71, 13, 10, 26, 10] : [255, 216, 255];
        if (!data.AsSpan().StartsWith(signature)) throw new InvalidOperationException("图片文件头与响应类型不符。");
        var output = outputStem + extension;
        await File.WriteAllBytesAsync(output, data, cancellationToken);
        return new JsonObject
        {
            ["file"] = Path.GetFileName(output), ["contentType"] = contentType,
            ["byteCount"] = data.Length, ["sha256"] = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()
        };
    }
}
