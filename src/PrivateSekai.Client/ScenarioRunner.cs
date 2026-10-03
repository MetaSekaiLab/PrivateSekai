using System.Text.Json;
using System.Text.Json.Nodes;

namespace PrivateSekai.Client;

public sealed class ScenarioRunner
{
    public static void Validate(Scenario scenario, IEnumerable<TargetConfiguration> targets, ISet<string> officialWrites)
    {
        if (scenario.Steps.Count == 0) throw new InvalidOperationException("场景没有步骤。");
        foreach (var target in targets)
        {
            target.Validate();
            if (target.ThumbnailBaseUrl != null)
                new TargetConfiguration { Kind = target.Kind, BaseUrl = target.ThumbnailBaseUrl }.Validate();
        }
        string? liveId = null;
        string? challengeLiveId = null;
        var previousOperation = "";
        foreach (var configured in scenario.Steps)
        {
            if (configured.DelayBeforeMs is < 0 or > 600_000)
                throw new InvalidOperationException("步骤等待时间须为 0 至 600000 毫秒。");
            var step = ResolveLiveSession(configured, liveId, challengeLiveId);
            if (!Operations.All.TryGetValue(step.Operation, out var definition))
                throw new InvalidOperationException("未知操作；使用 list 查看支持的操作。");
            if (step.RequireReleaseConditionIds.Any(id => id <= 0) ||
                (step.RequireReleaseConditionIds.Length > 0 && (!definition.IsWrite || !definition.Snapshot)))
                throw new InvalidOperationException("解锁条件须为正整数，且仅用于有状态回读的写操作。");
            if (step.Operation == "thumbnail-download")
            {
                if (previousOperation is "" or "thumbnail-download" || step.ThumbnailPathPointer?.StartsWith('/') != true)
                    throw new InvalidOperationException("图片下载需要上一条协议响应及 thumbnailPath 的 JSON Pointer。");
            }
            else if (step.ThumbnailPathPointer != null)
                throw new InvalidOperationException("只有图片下载支持 thumbnailPathPointer。");
            if (step.Operation is "inherit-preview" or "inherit-execute")
            {
                if (step.Args.ContainsKey("inheritId")) throw new InvalidOperationException("引继 ID 从私有账号档案读取。");
                _ = Operations.Path(definition, new() { Args = new(step.Args) { ["inheritId"] = "validation-inherit" }, Query = step.Query, QueryLists = step.QueryLists }, 0);
            }
            else _ = Operations.Path(definition, step, 0);
            // 登录凭证来自内存或本地账号档案，不允许写入场景。
            if (step.Operation is "auth" or "inherit-set")
            {
                if (step.Body != null) throw new InvalidOperationException("登录和引继设置不接受场景内凭证。");
            }
            else _ = Operations.EncodeBody(definition, step.Body);
            if (targets.Any(t => t.Kind == "official") && definition.IsWrite && !officialWrites.Contains(step.Operation))
                throw new InvalidOperationException($"official 写操作 {step.Operation} 未在本次命令中启用。");
            if (step.Operation == "live-start") liveId = "validation-live";
            if (step.Operation == "challenge-live-start") challengeLiveId = "validation-challenge";
            previousOperation = step.Operation;
        }
    }

    public static async Task Run(ProtocolClient client, Scenario scenario, string directory)
    {
        Directory.CreateDirectory(directory);
        client.CaptureTo(Path.Combine(directory, "http"));
        await client.AcquireSignature();
        string? liveId = null;
        string? challengeLiveId = null;
        JsonObject? previousResponse = null;
        for (var i = 0; i < scenario.Steps.Count; i++)
        {
            var step = scenario.Steps[i];
            var definition = Operations.All[step.Operation];
            var capture = new JsonObject { ["step"] = i + 1, ["operation"] = step.Operation, ["status"] = "started" };
            var path = Path.Combine(directory, $"{i + 1:D3}.json");
            var phase = "prepare";
            try
            {
                if (step.DelayBeforeMs > 0)
                {
                    phase = "wait";
                    capture["delayBeforeMs"] = step.DelayBeforeMs;
                    capture["status"] = "waiting";
                    JsonFiles.Write(path, capture);
                    Console.WriteLine($"步骤 {i + 1}: 等待 {step.DelayBeforeMs} 毫秒。");
                    await Task.Delay(step.DelayBeforeMs);
                    capture["status"] = "started";
                }
                phase = "resolve-session";
                step = ResolveLiveSession(step, liveId, challengeLiveId);
                if (definition.IsWrite && definition.Snapshot)
                {
                    phase = "before-snapshot";
                    var before = await client.Suite();
                    capture["before"] = client.Redactor.Clean(before);
                    if (step.RequireReleaseConditionIds.Length > 0)
                    {
                        phase = "release-condition";
                        var released = before["userReleaseConditions"]?.AsArray()
                            .Select(item => item?["releaseConditionId"]?.GetValue<int>()).ToHashSet() ?? [];
                        var missing = step.RequireReleaseConditionIds.Where(id => !released.Contains(id)).Distinct().ToArray();
                        capture["requiredReleaseConditionIds"] = JsonSerializer.SerializeToNode(step.RequireReleaseConditionIds);
                        capture["missingReleaseConditionIds"] = JsonSerializer.SerializeToNode(missing);
                        if (missing.Length > 0)
                            throw new ClientFailure("前置解锁条件未满足，未发送业务请求。");
                    }
                }
                phase = "prepare-request";
                capture["request"] = client.Redactor.Clean(step.Body);
                capture["args"] = client.Redactor.Clean(JsonSerializer.SerializeToNode(step.Args));
                capture["query"] = client.Redactor.Clean(JsonSerializer.SerializeToNode(step.Query));
                capture["queryLists"] = client.Redactor.Clean(JsonSerializer.SerializeToNode(step.QueryLists));
                if (step.ThumbnailPathPointer != null) capture["sourcePointer"] = step.ThumbnailPathPointer;
                JsonFiles.Write(path, capture);
                phase = "request";
                var response = step.Operation == "thumbnail-download"
                    ? await client.DownloadThumbnail(
                        Comparison.Resolve(previousResponse, step.ThumbnailPathPointer ?? "")?.GetValue<string>()
                            ?? throw new InvalidOperationException("上一条响应中不存在图片路径。"),
                        Path.Combine(directory, $"{i + 1:D3}"))
                    : step.Operation == "suite" ? await client.Suite() : await client.Send(step);
                previousResponse = response;
                phase = "response";
                capture["httpStatus"] = client.LastHttpStatus;
                if (step.Operation == "live-start")
                    liveId = response["userLiveId"]?.GetValue<string>()
                        ?? throw new InvalidOperationException("开局响应缺少 Live ID。");
                if (step.Operation == "challenge-live-start")
                    challengeLiveId = response["userChallengeLiveId"]?.GetValue<string>()
                        ?? throw new InvalidOperationException("开局响应缺少挑战 Live ID。");
                capture["response"] = client.Redactor.Clean(response);
                capture["status"] = "response-received";
                JsonFiles.Write(path, capture);
                if (definition.IsWrite && definition.Snapshot)
                {
                    phase = "after-snapshot";
                    capture["after"] = client.Redactor.Clean(await client.Suite());
                    capture["stateChanges"] = JsonSerializer.SerializeToNode(Comparison.Diff(
                        Comparison.Normalize(capture["before"]), Comparison.Normalize(capture["after"])), JsonFiles.Options);
                }
                phase = "assertion";
                foreach (var expectation in step.Expect)
                    if (!JsonNode.DeepEquals(Comparison.Resolve(response, expectation.Key), expectation.Value))
                        throw new InvalidOperationException("响应断言失败。");
                capture["status"] = "completed";
                JsonFiles.Write(path, capture);
                Console.WriteLine($"步骤 {i + 1}: {step.Operation} 完成");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // 只存异常类型，避免 URL、会话或服务端错误正文混入报告。
                capture["status"] = "stopped";
                capture["failurePhase"] = phase;
                capture["errorType"] = ex.GetType().Name;
                if (FailureDiagnostics.Transport(ex) is { } transportError)
                    capture["transportError"] = transportError;
                capture["lastHttpStatus"] = client.LastHttpStatus;
                capture["lastResponseHeaderNames"] = JsonSerializer.SerializeToNode(client.LastResponseHeaderNames);
                capture["lastResponse"] = client.LastResponse?.DeepClone();
                JsonFiles.Write(path, capture);
                throw;
            }
        }
    }

    private static ScenarioStep ResolveLiveSession(ScenarioStep step, string? liveId, string? challengeLiveId)
    {
        if (!step.UseLiveSession) return step;
        var challenge = step.Operation == "challenge-live-clear";
        var sessionId = challenge ? challengeLiveId : liveId;
        var sessionKey = challenge ? "userChallengeLiveId" : "userLiveId";
        if (step.Operation is not ("live-clear" or "live-voice" or "challenge-live-clear") || string.IsNullOrEmpty(sessionId))
            throw new InvalidOperationException("当前操作不能引用 Live 会话，或场景尚未成功开局。");
        if (step.Args.ContainsKey(sessionKey) || step.Body?.ContainsKey(sessionKey) == true)
            throw new InvalidOperationException("自动引用 Live 会话时不能同时指定 userLiveId。");
        var resolved = new ScenarioStep
        {
            Operation = step.Operation, Args = new(step.Args), Query = new(step.Query), QueryLists = new(step.QueryLists),
            Body = step.Body?.DeepClone().AsObject(), Expect = step.Expect, DelayBeforeMs = step.DelayBeforeMs,
            RequireReleaseConditionIds = step.RequireReleaseConditionIds
        };
        if (step.Operation is "live-clear" or "challenge-live-clear") resolved.Args[sessionKey] = sessionId;
        else if (resolved.Body != null) resolved.Body[sessionKey] = sessionId;
        else throw new InvalidOperationException("语音操作需要请求体。");
        return resolved;
    }

    public static JsonObject Compare(JsonObject left, JsonObject right)
    {
        if (left["operation"]?.GetValue<string>() != right["operation"]?.GetValue<string>())
            throw new InvalidOperationException("两个记录的操作不同。");
        var leftHttpStatus = ResponseValue(left, "httpStatus", "lastHttpStatus");
        var rightHttpStatus = ResponseValue(right, "httpStatus", "lastHttpStatus");
        var hasSnapshots = left["before"] != null && left["after"] != null && right["before"] != null && right["after"] != null;
        var result = new JsonObject
        {
            ["operation"] = left["operation"]?.DeepClone(),
            ["leftStatus"] = left["status"]?.DeepClone(), ["rightStatus"] = right["status"]?.DeepClone(),
            ["httpStatusDifferences"] = leftHttpStatus != null && rightHttpStatus != null
                ? JsonSerializer.SerializeToNode(Comparison.Diff(leftHttpStatus, rightHttpStatus), JsonFiles.Options) : null,
            ["baselineDifferences"] = JsonSerializer.SerializeToNode(Comparison.Diff(Comparison.Normalize(left["before"]), Comparison.Normalize(right["before"])), JsonFiles.Options),
            ["responseDifferences"] = JsonSerializer.SerializeToNode(Comparison.Diff(
                Comparison.Normalize(ResponseValue(left, "response", "lastResponse")),
                Comparison.Normalize(ResponseValue(right, "response", "lastResponse"))), JsonFiles.Options),
            ["deltaDifferences"] = hasSnapshots ? JsonSerializer.SerializeToNode(Comparison.Diff(
                Comparison.DeltaView(Comparison.Diff(Comparison.Normalize(left["before"]), Comparison.Normalize(left["after"]))),
                Comparison.DeltaView(Comparison.Diff(Comparison.Normalize(right["before"]), Comparison.Normalize(right["after"])))), JsonFiles.Options) : null
        };
        result["complete"] = left["status"]?.GetValue<string>() == "completed" && right["status"]?.GetValue<string>() == "completed";
        return result;
    }

    private static JsonNode? ResponseValue(JsonObject record, string field, string fallback) =>
        record[field] ?? (record["failurePhase"]?.GetValue<string>() == "request" ? record[fallback] : null);
}
