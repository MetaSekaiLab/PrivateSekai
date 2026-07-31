using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Config;
using PrivateSekai.Crypto;
using System.Collections.Concurrent;

namespace PrivateSekai.Controllers;

[ApiController]
public class SuiteMasterFileController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheLocks = new();

    private readonly ILogger<SuiteMasterFileController> _logger;

    public SuiteMasterFileController(ILogger<SuiteMasterFileController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// GET /api/suitemasterfile/{version}/{**filename}
    /// </summary>
    [HttpGet("api/suitemasterfile/{version}/{**filename}")]
    public async Task HandleSuiteMasterFile(string version, string filename)
    {
        string filePath;
        string cachePath;

        try
        {
            var suiteMasterRoot = Path.GetFullPath(ServerConfig.SuiteMasterFilePath);
            var fileRoot = Path.GetFullPath(Path.Combine(suiteMasterRoot, version));
            filePath = Path.GetFullPath(Path.Combine(fileRoot, filename));

            var cacheBaseRoot = Path.GetFullPath(Path.Combine(suiteMasterRoot, ".encrypted-cache"));
            var cacheRoot = Path.GetFullPath(Path.Combine(cacheBaseRoot, version));
            cachePath = Path.GetFullPath(Path.Combine(cacheRoot, filename));

            if (!IsUnderDirectory(fileRoot, suiteMasterRoot)
                || !IsUnderDirectory(cacheRoot, cacheBaseRoot)
                || !IsUnderDirectory(filePath, fileRoot)
                || !IsUnderDirectory(cachePath, cacheRoot))
            {
                _logger.LogWarning(
                    "Rejected suite master file path: version={Version}, filename={Filename}",
                    version,
                    filename);
                Response.StatusCode = 404;
                await Response.WriteAsync("Not Found");
                return;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            _logger.LogWarning(
                "Invalid suite master file path: version={Version}, filename={Filename}",
                version,
                filename);
            Response.StatusCode = 404;
            await Response.WriteAsync("Not Found");
            return;
        }

        if (!System.IO.File.Exists(filePath))
        {
            _logger.LogError("File not found: {FilePath}", filePath);
            Response.StatusCode = 404;
            await Response.WriteAsync("Not Found");
            return;
        }

        try
        {
            cachePath = await GetEncryptedCachePathAsync(filePath, cachePath);
            var cacheInfo = new FileInfo(cachePath);

            Response.ContentType = "application/octet-stream";
            Response.ContentLength = cacheInfo.Length;

            _logger.LogInformation(
                "Serving encrypted suite master file: {FilePath} -> {CachePath} ({Length} bytes)",
                filePath,
                cachePath,
                cacheInfo.Length);

            await using var stream = System.IO.File.OpenRead(cachePath);
            await stream.CopyToAsync(Response.Body, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // 客户端断开连接
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file: {FilePath}", filePath);
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
                await Response.WriteAsync("Failed to process file");
            }
        }
    }

    private async Task<string> GetEncryptedCachePathAsync(string filePath, string cachePath)
    {
        var cacheLock = CacheLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));

        await cacheLock.WaitAsync(HttpContext.RequestAborted);
        try
        {
            if (IsEncryptedCacheFresh(filePath, cachePath))
                return cachePath;

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            var tempPath = $"{cachePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                _logger.LogInformation("Building encrypted suite master cache: {FilePath} -> {CachePath}", filePath, cachePath);
                await using (var source = System.IO.File.OpenRead(filePath))
                await using (var destination = System.IO.File.Create(tempPath))
                {
                    PrskCrypto.PrskEncJsonStreamToStream(source, destination);
                }

                if (System.IO.File.Exists(cachePath))
                    System.IO.File.Delete(cachePath);
                System.IO.File.Move(tempPath, cachePath);

                var sourceInfo = new FileInfo(filePath);
                System.IO.File.SetLastWriteTimeUtc(cachePath, sourceInfo.LastWriteTimeUtc);
                return cachePath;
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private static bool IsEncryptedCacheFresh(string sourcePath, string cachePath)
    {
        if (!System.IO.File.Exists(cachePath))
            return false;

        var sourceInfo = new FileInfo(sourcePath);
        var cacheInfo = new FileInfo(cachePath);
        if (cacheInfo.Length == 0)
            return false;

        return cacheInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        if (!Path.EndsInDirectorySeparator(fullDirectory))
            fullDirectory += Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return Path.GetFullPath(path).StartsWith(fullDirectory, comparison);
    }
}
