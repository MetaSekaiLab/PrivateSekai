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
        var master = JsonNode.Parse("""
            {"playLevelScores":[{"liveType":"solo","playLevel":5,"b":450000}],"cards":[],
             "mysekaiStaminaRecovery":{"id":1,"boostQuantity":1,"recoveryBoostStamina":100},
             "mysekaiColorfulPass":{"id":1,"expireDays":30,"mysekaiConvertFixtureConvertMinutesDecreaseRate":0.5,"mysekaiSiteHousingPresetSlotExpireDays":365},
             "mysekaiSiteHousingPreset":{"id":1,"slotNoLowerLimit":1,"slotNoUpperLimit":3,"slotNoUpperLimitForMysekaiColorfulPass":10},
             "mysekaiFixtureGameCharacterPerformanceBonusLimit":{"id":1,"bonusRateLimit":100},
             "mysekaiConvertFixtureSlot":{"id":1,"slotNoLowerLimit":1,"slotNoUpperLimit":1,"slotNoUpperLimitForMysekaiColorfulPass":3}}
            """)!;
        File.WriteAllText(Path.Combine(directory, "9.2.3.4", filename), master.ToJsonString());
        var response = await client.Send(step);
        check(response["playLevelScores"]![0]!["b"]!.GetValue<int>() == 450000 && response["cards"] is JsonArray,
            "真实分片控制器的加密响应解密为多个 master 表，允许空表");
        foreach (var (name, value) in master.AsObject().Where(pair => pair.Value is JsonObject))
            check(JsonNode.DeepEquals(response[name], value), "master单对象配置原样读取：" + name);
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
        foreach (var error in new[] { """{"errorCode":"fixture_error"}""", """{"errorCode":{},"cards":[]}""",
            """{"cards":[],"invalidScalar":42}""", """{"cards":null}""", "{}" })
        {
            var errorFile = "01_" + new string('b', 64);
            File.WriteAllText(Path.Combine(directory, "9.2.3.4", errorFile), error);
            var failed = false;
            using var errorClient = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await errorClient.Send(new() { Operation = "system" });
            try
            {
                await errorClient.Send(new() { Operation = "suite-master-split",
                    Args = new() { ["dataVersion"] = "9.2.3.4", ["splitFile"] = errorFile } });
            }
            catch (InvalidOperationException) { failed = true; }
            check(failed, "master 分片拒绝错误对象、标量、null及空根对象");
        }
        await client.Send(new() { Operation = "system" });
    }
}
