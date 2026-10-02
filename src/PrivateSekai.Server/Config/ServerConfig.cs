using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PrivateSekai.Config;

public static class ServerConfig
{
    public static byte[] AesKey { get; private set; } = null!;
    public static byte[] AesIv  { get; private set; } = null!;
    public static string JwtKey { get; private set; } = null!;
    public static byte[] EmptyRequestCiphertext { get; private set; } = null!;

    public static bool IgnoreInvalidCredential { get; private set; }
    public static bool SkipTutorial { get; private set; }
    public static bool Debug { get; private set; }

    public static int Port { get; private set; }

    public static string GameVersionDomain { get; private set; } = null!;

    public static string TemplatePath { get; private set; } = null!;
    public static string SuiteMasterFilePath { get; private set; } = null!;
    public static string SekaiMasterDbDiffPath { get; private set; } = null!;
    public static MasterCacheConfig MasterCache { get; private set; } = new();

    public static void Load(IConfiguration config, string contentRootPath)
    {
        var s = config.GetSection("PrivateSekai");
        if (!s.Exists())
            throw new InvalidOperationException("Missing 'PrivateSekai' section in appsettings.json");

        AesKey      = Encoding.UTF8.GetBytes(Require(s, "AesKey"));
        AesIv       = Encoding.UTF8.GetBytes(Require(s, "AesIv"));
        JwtKey      = Require(s, "JwtKey");
        EmptyRequestCiphertext = Convert.FromHexString(Require(s, "EmptyRequestCiphertext"));

        IgnoreInvalidCredential = bool.Parse(Require(s, "IgnoreInvalidCredential"));
        SkipTutorial            = bool.Parse(Require(s, "SkipTutorial"));
        Debug                   = bool.Parse(Require(s, "Debug"));
        Port                    = int.Parse(Require(s, "Port"));

        GameVersionDomain        = Require(s, "GameVersionDomain");

        var paths = s.GetSection("Paths");
        TemplatePath          = ResolvePath(Require(paths, "Template"), contentRootPath);
        SuiteMasterFilePath   = ResolvePath(Require(paths, "SuiteMasterFile"), contentRootPath);
        SekaiMasterDbDiffPath = ResolvePath(Require(paths, "SekaiMasterDbDiff"), contentRootPath);

        MasterCache = s.GetSection("MasterCache").Get<MasterCacheConfig>() ?? new MasterCacheConfig();
    }

    private static string ResolvePath(string path, string contentRootPath)
    {
        if (Path.IsPathFullyQualified(path))
            return Path.GetFullPath(path);

        var contentRootPathCandidate = Path.GetFullPath(Path.Combine(contentRootPath, path));
        if (Directory.Exists(contentRootPathCandidate) || File.Exists(contentRootPathCandidate))
            return contentRootPathCandidate;

        var repositoryRoot = FindRepositoryRoot(contentRootPath);
        return Path.GetFullPath(Path.Combine(repositoryRoot, path));
    }

    private static string FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return startPath;
    }

    private static string Require(IConfigurationSection section, string key) =>
        section[key] ?? throw new InvalidOperationException(
            $"Missing required config: PrivateSekai:{key}");
}

