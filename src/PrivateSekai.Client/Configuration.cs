using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PrivateSekai.Client;

public sealed class ClientConfiguration
{
    public Dictionary<string, TargetConfiguration> Targets { get; set; } = [];
}

public sealed class TargetConfiguration
{
    public string Kind { get; set; } = "local";
    public string BaseUrl { get; set; } = "";
    public string? ThumbnailBaseUrl { get; set; }
    public long UserId { get; set; }
    public string CryptoSettings { get; set; } = "";
    public string? HeadersEnv { get; set; }
    public string? CredentialEnv { get; set; }
    public string? InheritPasswordEnv { get; set; }
    public string? InheritSigningKeyEnv { get; set; }
    public string? HeadersFile { get; set; }
    public string? AccountFile { get; set; }
    public string? SignatureUrl { get; set; }
    public bool RequireRotatingToken { get; set; }

    public Uri Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new InvalidOperationException("目标地址必须是无凭证、无路径的 HTTP(S) origin。");
        if (Kind is not ("local" or "official"))
            throw new InvalidOperationException("目标 kind 必须为 local 或 official。");
        if (Kind == "local" && !uri.IsLoopback)
            throw new InvalidOperationException("local 目标只允许回环地址；远端必须显式标记 official。");
        if (Kind == "official" && uri.Scheme != "https")
            throw new InvalidOperationException("official 目标必须使用 HTTPS。");
        return uri;
    }
}

public sealed class Scenario
{
    public List<ScenarioStep> Steps { get; set; } = [];
}

public sealed class ScenarioStep
{
    public string Operation { get; set; } = "";
    public bool UseLiveSession { get; set; }
    public int DelayBeforeMs { get; set; }
    public int[] RequireReleaseConditionIds { get; set; } = [];
    public string? ThumbnailPathPointer { get; set; }
    public Dictionary<string, string> Args { get; set; } = [];
    public Dictionary<string, string> Query { get; set; } = [];
    public Dictionary<string, int[]> QueryLists { get; set; } = [];
    public JsonObject? Body { get; set; }
    public Dictionary<string, JsonNode?> Expect { get; set; } = [];
}

public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidOperationException("配置或场景为空。");

    public static void Write(string path, object value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
}
