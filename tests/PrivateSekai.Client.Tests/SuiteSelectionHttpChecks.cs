extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

internal static class SuiteSelectionHttpChecks
{
    public static async Task Run(TargetConfiguration config, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        const long userId = 24680;
        var data = new SuiteUser
        {
            userRegistration = new() { userId = userId }, userGamedata = new() { userId = userId, coin = 7 },
            userCards = [new() { cardId = 1 }], userReleaseConditions = [new() { releaseConditionId = 9 }],
            userEventEpisodeStatuses = [new() { episodeId = 1, status = "unreleased" }],
            userPresents = [], userChallengeLiveSoloDecks = [], refreshableTypes = ["userCards"]
        };
        store.Save(userId, new UserState { Data = data });
        var before = DumpSerializer.Serialize(store.Read(userId)!.Data);
        using var client = new ProtocolClient(new TargetConfiguration { BaseUrl = config.BaseUrl, UserId = userId,
            RequireRotatingToken = true, HeadersFile = "headers.json" }, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await client.Send(new() { Operation = "system" });
        var native = await client.Send(new() { Operation = "suite-story-release" });
        check(native.ContainsKey("userReleaseConditions") && !native.ContainsKey("userEventEpisodeStatuses") &&
            !native.ContainsKey("userRegistration"), "剧情Suite GET通过真实控制器选择条件且忽略未知驼峰组");
        check(native["userChallengeLiveSoloDecks"] is JsonArray && native["refreshableTypes"]!.AsArray().Count == 0,
            "按名称Suite加密HTTP保留公共挑战空数组并省略待刷新字段");
        var selected = await client.Send(new() { Operation = "suite-select", Args = new() { ["resourceNames"] = "user_card,user_release_condition,user_card" } });
        check(selected.ContainsKey("userCards") && selected.ContainsKey("userReleaseConditions") && !selected.ContainsKey("userGamedata"),
            "通用选择操作正确编码逗号名称并返回所选组联合");
        var unknown = await client.Send(new() { Operation = "suite-select", Args = new() { ["resourceNames"] = "unknown,USER_RELEASE_CONDITION" } });
        check(unknown.ContainsKey("now") && unknown.ContainsKey("userPresents") && !unknown.ContainsKey("userReleaseConditions"),
            "未知或大小写不匹配的Suite名称只返回公共数据");
        check(before.SequenceEqual(DumpSerializer.Serialize(store.Read(userId)!.Data)), "连续加密Suite选择查询不保存或清除用户状态");
        foreach (var invalid in new[] { "", "user_card&isForceAllReload=true", "user_card,user card", "user_card,", "../user_card" })
        {
            var failed = false;
            try { Operations.Path(Operations.All["suite-select"], new() { Args = new() { ["resourceNames"] = invalid } }, userId); }
            catch (InvalidOperationException) { failed = true; }
            check(failed, "Suite名称拒绝空值、分隔符及查询参数注入");
        }
        var definitions = (IDictionary<string, OperationDefinition>)Operations.All;
        foreach (var (operation, query) in new[] { ("suite-no-names-check", "?isForceAllReload=false"),
            ("suite-force-check", "?isForceAllReload=true&name=user_card"),
            ("suite-default-force-check", "?name=user_card") })
        {
            definitions.Add(operation, new("GET", "/api/suite/user/{userId}" + query, null, "now", false));
            var result = await client.Send(new() { Operation = operation });
            check(result.ContainsKey("userRegistration") == (operation != "suite-no-names-check"),
                "显式false无名称只读公共集，省略参数或true均保持完整Suite");
            definitions.Remove(operation);
        }
        var missing = new ScenarioStep { Operation = "suite-select" };
        var missingFailed = false;
        try { Operations.Path(Operations.All["suite-select"], missing, userId); }
        catch (InvalidOperationException) { missingFailed = true; }
        check(missingFailed, "通用Suite选择操作要求显式提供名称");
    }
}
