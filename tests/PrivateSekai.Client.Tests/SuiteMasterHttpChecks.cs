using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;

internal static class SuiteMasterHttpChecks
{
    public static async Task Run(ProtocolClient client, TargetConfiguration config, string directory, Action<bool, string> check)
    {
        var definition = Operations.All["suite-master-split"];
        var filename = "00_" + new string('a', 64);
        var step = new ScenarioStep
        {
            Operation = "suite-master-split",
            Args = new() { ["dataVersion"] = "9.2.3.4", ["splitFile"] = filename }
        };
        check(!definition.IsWrite && !definition.Snapshot &&
            Operations.Path(definition, step, 1) == "/api/suitemasterfile/9.2.3.4/" + filename,
            "master 分片按版本和完整文件名读取，不绑定某个版本或表名");
        Directory.CreateDirectory(Path.Combine(directory, "9.2.3.4"));
        File.WriteAllText(Path.Combine(directory, "9.2.3.4", filename),
            """{"playLevelScores":[{"liveType":"solo","playLevel":5,"b":450000}],"cards":[]}""");
        var response = await client.Send(step);
        check(response["playLevelScores"]![0]!["b"]!.GetValue<int>() == 450000 && response["cards"] is JsonArray,
            "真实分片控制器的加密响应解密为多个 master 表，允许空表");
        check(!client.LastResponseHeaderNames.Contains("X-Session-Token", StringComparer.OrdinalIgnoreCase),
            "master 静态响应不要求返回轮换 token");
        await client.Send(new() { Operation = "suite" });
        check(client.LastHttpStatus == 200, "读取 master 后业务请求继续使用原会话");
        foreach (var (key, invalid) in new[]
        {
            ("dataVersion", "../9.2.3.4"), ("dataVersion", "9.2.3"),
            ("splitFile", "../" + filename), ("splitFile", filename + "\n")
        })
        {
            var args = new Dictionary<string, string>(step.Args) { [key] = invalid };
            var rejected = false;
            try { Operations.Path(definition, new() { Args = args }, 1); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected, "master 分片拒绝非完整版本、路径穿越及额外字符");
        }
        var errorFile = "01_" + new string('b', 64);
        File.WriteAllText(Path.Combine(directory, "9.2.3.4", errorFile), """{"errorCode":"fixture_error"}""");
        var failed = false;
        using var errorClient = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await errorClient.Send(new() { Operation = "system" });
        try
        {
            await errorClient.Send(new() { Operation = "suite-master-split",
                Args = new() { ["dataVersion"] = "9.2.3.4", ["splitFile"] = errorFile } });
        }
        catch (InvalidOperationException) { failed = true; }
        check(failed, "master 分片不能把成功 HTTP 中的错误对象当作数据表");
        await client.Send(new() { Operation = "system" });
    }
}
