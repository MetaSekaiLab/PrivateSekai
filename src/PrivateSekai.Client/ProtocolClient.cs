using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MessagePack;
using PrivateSekai.Transport;

namespace PrivateSekai.Client;

public sealed class ClientFailure(string message) : Exception(message);

public sealed class TestAccount
{
    public string Origin { get; set; } = "";
    public long UserId { get; set; }
    public string Credential { get; set; } = "";
    public bool CreatedByMockClient { get; set; }
    public string? InheritId { get; set; }
    public string? InheritPassword { get; set; }
}

public sealed class ProtocolClient : IDisposable
{
    private readonly HttpClient http;
    private readonly TargetConfiguration config;
    private readonly byte[] key;
    private readonly byte[] iv;
    private readonly Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? accountPath;
    private readonly FileStream? accountLock;
    private readonly SemaphoreSlim serial = new(1);
    private bool poisoned;
    private string? credential;
    private TestAccount? savedAccount;
    public long UserId { get; private set; }
    public bool OwnTestAccount { get; private set; }
    public Redactor Redactor { get; } = new();
    public JsonObject? LastResponse { get; private set; }
    public int? LastHttpStatus { get; private set; }
    public string? LastLoginBonusStatus { get; private set; }
    public string[] LastResponseHeaderNames { get; private set; } = [];

    public ProtocolClient(TargetConfiguration config, string configDirectory, byte[]? testKey = null, byte[]? testIv = null)
    {
        this.config = config;
        var origin = config.Validate();
        http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(45) };
        if (testKey != null && testIv != null) { key = testKey; iv = testIv; }
        else
        {
            var settings = JsonNode.Parse(File.ReadAllText(System.IO.Path.GetFullPath(config.CryptoSettings, configDirectory)))!["PrivateSekai"]!;
            key = Encoding.UTF8.GetBytes(settings["AesKey"]!.GetValue<string>());
            iv = Encoding.UTF8.GetBytes(settings["AesIv"]!.GetValue<string>());
        }
        if (key.Length is not (16 or 24 or 32) || iv.Length != 16)
            throw new ClientFailure("AES 配置长度错误；示例配置的占位符不能用于实际请求。");
        UserId = config.UserId;
        if (config.HeadersFile != null)
            LoadHeaders(JsonFiles.Read<Dictionary<string, string>>(System.IO.Path.GetFullPath(config.HeadersFile, configDirectory)));
        if (config.HeadersEnv != null)
            LoadHeaders(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(RequiredEnvironment(config.HeadersEnv))!);
        if (config.CredentialEnv != null) credential = RequiredEnvironment(config.CredentialEnv);
        if (config.AccountFile != null)
        {
            accountPath = System.IO.Path.GetFullPath(config.AccountFile, configDirectory);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(accountPath)!);
            accountLock = new FileStream(accountPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(accountPath))
            {
                var account = JsonFiles.Read<TestAccount>(accountPath);
                savedAccount = account;
                if (account.Origin != origin.GetLeftPart(UriPartial.Authority))
                    throw new InvalidOperationException("账号档案与目标地址不匹配。");
                UserId = account.UserId;
                credential = account.Credential;
                OwnTestAccount = account.CreatedByMockClient;
                Redactor.AddSecret(account.InheritId);
                Redactor.AddSecret(account.InheritPassword);
            }
        }
        Redactor.AddSecret(credential);
        Redactor.AddAccount(UserId);
    }

    private void LoadHeaders(Dictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            if (pair.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                pair.Value.Contains('\r') || pair.Value.Contains('\n'))
                throw new InvalidOperationException("不允许覆盖路由或正文长度 header。");
            headers[pair.Key] = pair.Value;
            if (Redactor.IsSensitive(pair.Key)) Redactor.AddSecret(pair.Value);
        }
    }

    public async Task AcquireSignature()
    {
        if (config.SignatureUrl == null) return;
        var uri = new Uri(config.SignatureUrl);
        if (uri.Scheme != "https" || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("签名地址必须使用 HTTPS 且不含凭证。");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        foreach (var header in headers.Where(h => !Redactor.IsSensitive(h.Key) && !h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        request.Content = new ByteArrayContent(PrskCrypto.EncryptAesCbc([], key, iv));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode || !response.Headers.TryGetValues("Set-Cookie", out var cookies))
            throw new ClientFailure($"签名请求未返回有效 cookie（HTTP {(int)response.StatusCode}）。");
        var parts = cookies.SelectMany(value => System.Text.RegularExpressions.Regex.Matches(value,
            @"CloudFront-[\w-]+=[^;,\s]+").Select(m => m.Value)).Distinct().ToArray();
        if (parts.Length != 3) throw new ClientFailure("签名 cookie 不完整。");
        headers["Cookie"] = string.Join("; ", parts);
        Redactor.AddSecret(headers["Cookie"]);
    }

    public async Task<JsonObject> Send(ScenarioStep step)
    {
        await serial.WaitAsync();
        try
        {
            if (poisoned) throw new InvalidOperationException("上次请求的会话状态不明，请重新登录；不会自动重试。");
            var definition = Operations.All[step.Operation];
            LastResponse = null;
            LastHttpStatus = null;
            LastResponseHeaderNames = [];
            if (step.Operation == "register" && (UserId != 0 || credential != null || (accountPath != null && File.Exists(accountPath))))
                throw new InvalidOperationException("目标已有账号，不重复注册。");
            if (step.Operation == "register" && config.Kind == "official" && accountPath == null)
                throw new InvalidOperationException("官方测试注册必须指定本地账号档案。");
            if (config.Kind == "official" && definition.IsWrite && step.Operation is not ("register" or "auth") && !OwnTestAccount)
                throw new InvalidOperationException("官方写操作仅用于本工具新注册并留档的测试账号。");
            var body = step.Body?.DeepClone().AsObject();
            string? inheritPassword = null;
            string? inheritToken = null;
            if (step.Operation.StartsWith("inherit-", StringComparison.Ordinal))
            {
                if (step.Body != null || step.Args.ContainsKey("inheritId"))
                    throw new InvalidOperationException("引继凭证不允许写入场景。");
                if (savedAccount == null || accountPath == null || savedAccount.UserId != UserId)
                    throw new InvalidOperationException("引继操作需要当前账号的私有档案。");
                if (step.Operation == "inherit-set")
                {
                    inheritPassword = RequiredEnvironment(config.InheritPasswordEnv ?? throw new InvalidOperationException("未配置引继密码环境变量。"));
                    if (inheritPassword.Length == 0) throw new InvalidOperationException("引继密码不能为空。");
                    Redactor.AddSecret(inheritPassword);
                    body = new JsonObject { ["password"] = inheritPassword };
                }
                else
                {
                    if (string.IsNullOrEmpty(savedAccount.InheritId) || string.IsNullOrEmpty(savedAccount.InheritPassword))
                        throw new InvalidOperationException("账号档案缺少已设置的引继信息。");
                    var signingKey = RequiredEnvironment(config.InheritSigningKeyEnv ?? throw new InvalidOperationException("未配置已核验的引继签名密钥。"));
                    if (signingKey.Length == 0) throw new InvalidOperationException("引继签名密钥不能为空。");
                    inheritToken = InheritToken.Create(savedAccount.InheritId, savedAccount.InheritPassword, signingKey);
                    Redactor.AddSecret(inheritToken);
                    step = new ScenarioStep { Operation = step.Operation, Args = new(step.Args) { ["inheritId"] = savedAccount.InheritId }, Query = step.Query, QueryLists = step.QueryLists };
                }
            }
            if (step.Operation == "auth")
            {
                if (string.IsNullOrEmpty(credential)) throw new InvalidOperationException("缺少账号凭证。");
                body = new JsonObject { ["credential"] = credential, ["deviceId"] = null, ["authTriggerType"] = "normal" };
            }
            if (step.Operation == "profile-save" && body != null)
            {
                if (body["userId"] != null && body["userId"]!.GetValue<long>() != UserId)
                    throw new InvalidOperationException("个人资料请求的账号与当前目标不一致。");
                body["userId"] = UserId;
            }
            if (step.Operation == "deck-save" && body?["userDeckUpdates"] is JsonArray deckUpdates)
                foreach (var update in deckUpdates)
                {
                    var deck = update?["userDeck"] as JsonObject
                        ?? throw new InvalidOperationException("编队更新缺少 userDeck。");
                    var deckUserId = deck["userId"]?.GetValue<long>() ?? 0;
                    if (deckUserId != 0 && deckUserId != UserId)
                        throw new InvalidOperationException("编队请求的账号与当前目标不一致。");
                    deck["userId"] = UserId;
                }
            var packed = Operations.EncodeBody(definition, body);
            var path = Operations.Path(definition, step, UserId);
            using var request = new HttpRequestMessage(new HttpMethod(definition.Method), path);
            foreach (var pair in headers)
                if (!pair.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            request.Headers.Remove("X-Request-Id");
            request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString());
            request.Headers.Remove("X-Inherit-Id-Verify-Token");
            if (inheritToken != null) request.Headers.Add("X-Inherit-Id-Verify-Token", inheritToken);
            if (request.Headers.Accept.Count == 0)
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            request.Content = new ByteArrayContent(packed == null ? [] : PrskCrypto.EncryptAesCbc(packed, key, iv));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var hadToken = headers.ContainsKey("X-Session-Token");
            // 请求离开进程后，任何异常都需要显式重新登录，而不是重复消费旧 token。
            poisoned = true;
            using var response = await http.SendAsync(request);
            LastHttpStatus = (int)response.StatusCode;
            LastLoginBonusStatus = response.Headers.TryGetValues("X-Login-Bonus-Status", out var loginStatus)
                ? loginStatus.Single() : null;
            LastResponseHeaderNames = response.Headers.Select(h => h.Key).OrderBy(k => k).ToArray();
            var ciphertext = await response.Content.ReadAsByteArrayAsync();
            if (!response.IsSuccessStatusCode)
            {
                LastResponse = ReadError(ciphertext);
                throw new ClientFailure($"{step.Operation} 返回 HTTP {(int)response.StatusCode}；已停止，不重试。");
            }
            var noContent = response.StatusCode == System.Net.HttpStatusCode.NoContent;
            if (noContent && (step.Operation != "story-read" ||
                step.Args.GetValueOrDefault("storyType") is not ("unit_story" or "card_story" or "special_story") || ciphertext.Length != 0))
                throw new InvalidOperationException("该操作尚未核验 HTTP 204 空响应；已停止。");
            var decoded = noContent ? new JsonObject() :
                JsonNode.Parse(MessagePackSerializer.ConvertToJson(PrskCrypto.DecryptAesCbc(ciphertext, key, iv))) as JsonObject
                ?? throw new InvalidOperationException("响应不是 MessagePack map。");
            LastResponse = Redactor.Clean(decoded)?.AsObject();
            if (!noContent && (definition.RequiredResponseField is { } requiredField ? !decoded.ContainsKey(requiredField) : decoded.Count != 0))
                throw new InvalidOperationException("响应结构不符合预期，可能为业务错误；已停止。");
            string? nextToken = null;
            if (response.Headers.TryGetValues("X-Session-Token", out var values)) nextToken = values.Single();
            if (step.Operation == "auth") nextToken ??= decoded["sessionToken"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(nextToken))
            {
                headers["X-Session-Token"] = nextToken;
                Redactor.AddSecret(nextToken);
            }
            if (step.Operation == "register")
            {
                UserId = decoded["userRegistration"]!["userId"]!.GetValue<long>();
                credential = decoded["credential"]!.GetValue<string>();
                Redactor.AddAccount(UserId);
                Redactor.AddSecret(credential);
                OwnTestAccount = true;
                if (accountPath != null)
                {
                    savedAccount = new TestAccount
                    {
                        Origin = http.BaseAddress!.GetLeftPart(UriPartial.Authority), UserId = UserId,
                        Credential = credential, CreatedByMockClient = true
                    };
                    using var output = new FileStream(accountPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    System.Text.Json.JsonSerializer.Serialize(output, savedAccount, JsonFiles.Options);
                }
            }
            if (step.Operation == "inherit-set")
            {
                var id = decoded["userInherit"]?["inheritId"]?.GetValue<string>();
                if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("引继设置响应缺少 ID。");
                Redactor.AddSecret(id);
                savedAccount!.InheritId = id;
                savedAccount.InheritPassword = inheritPassword;
                SaveAccount();
            }
            if (step.Operation is "inherit-preview" or "inherit-execute")
            {
                if (decoded["afterUserGamedata"]?["userId"]?.GetValue<long>() != UserId)
                    throw new InvalidOperationException("引继响应与当前留档账号不一致。");
                if (step.Operation == "inherit-execute")
                {
                    credential = decoded["credential"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(credential)) throw new InvalidOperationException("引继执行响应缺少凭证。");
                    Redactor.AddSecret(credential);
                    savedAccount!.Credential = credential;
                    SaveAccount();
                }
            }
            if (step.Operation == "auth")
            {
                foreach (var field in new[] { "appVersion", "dataVersion", "assetVersion" })
                    if (decoded[field] is JsonValue value)
                        headers[field switch { "appVersion" => "X-App-Version", "dataVersion" => "X-Data-Version", _ => "X-Asset-Version" }] = value.GetValue<string>();
            }
            LastResponse = Redactor.Clean(decoded)?.AsObject();
            if ((config.Kind == "official" || config.RequireRotatingToken) && hadToken && string.IsNullOrEmpty(nextToken))
                throw new InvalidOperationException("响应未提供下一枚会话 token；停止后续请求，需核验响应头。");
            poisoned = false;
            return decoded;
        }
        finally { serial.Release(); }
    }

    public async Task<JsonObject> Suite()
    {
        var suite = await Send(new ScenarioStep { Operation = "suite" });
        if (suite["userRegistration"]?["userId"]?.GetValue<long>() != UserId)
            throw new InvalidOperationException("SuiteUser 与目标账号不一致，停止操作。");
        return suite;
    }

    public async Task<JsonObject> DownloadThumbnail(string path, string outputStem)
    {
        await serial.WaitAsync();
        try
        {
            LastResponse = null;
            LastHttpStatus = null;
            LastResponseHeaderNames = [];
            if (poisoned) throw new InvalidOperationException("API 会话状态不明，已停止场景。");
            var origin = new TargetConfiguration { Kind = config.Kind, BaseUrl = config.ThumbnailBaseUrl ?? config.BaseUrl }.Validate();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, ThumbnailDownload.ValidatePath(path)));
            // 普通图片请求不复制 API 凭证、会话或签名 header。
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            LastHttpStatus = (int)response.StatusCode;
            LastResponseHeaderNames = response.Headers.Select(h => h.Key).OrderBy(k => k).ToArray();
            var result = await ThumbnailDownload.Save(response, outputStem, timeout.Token);
            LastResponse = result.DeepClone().AsObject();
            return result;
        }
        finally { serial.Release(); }
    }

    private static string RequiredEnvironment(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException("缺少指定的环境变量。");

    private void SaveAccount()
    {
        var pending = accountPath! + ".pending";
        JsonFiles.Write(pending, savedAccount!);
        File.Move(pending, accountPath!, overwrite: true);
    }

    private JsonObject ReadError(byte[] bytes)
    {
        try
        {
            return Redactor.Clean(JsonNode.Parse(MessagePackSerializer.ConvertToJson(PrskCrypto.DecryptAesCbc(bytes, key, iv))))?.AsObject()
                ?? new JsonObject { ["byteCount"] = bytes.Length };
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or MessagePackSerializationException or System.Text.Json.JsonException or InvalidOperationException)
        {
            // 非协议错误页不保存原文，避免网关或代理信息泄露。
            return new JsonObject { ["byteCount"] = bytes.Length, ["format"] = "non-protocol-error" };
        }
    }

    public void Dispose()
    {
        http.Dispose();
        accountLock?.Dispose();
        serial.Dispose();
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(iv);
    }
}
