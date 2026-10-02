using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class ProfileHttpChecks
{
    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userProfile = new();
        store.Save(1, state);
        var scenario = new Scenario { Steps =
        [
            new() { Operation = "profile-save", Body = JsonNode.Parse("""
                {"word":"fixture-profile-word","twitterId":"fixture-social-id","profileImageType":"card","profileImageId":1}
                """)!.AsObject(), Expect = new() { ["/updatedResources/userProfile/profileImageId"] = JsonValue.Create(1) } }
        ] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "profile"));
        var saved = store.Read(1)!.Data.userProfile;
        check(saved.word == "fixture-profile-word" && saved.twitterId == "fixture-social-id" && saved.profileImageId == 1,
            "个人资料客户端真实 HTTP 保存留言、社交 ID 和头像");
        var record = File.ReadAllText(Path.Combine(directory, "profile/001.json"));
        check(!record.Contains("fixture-profile-word") && !record.Contains("fixture-social-id"),
            "个人资料请求、响应和状态快照中的文本均脱敏");
        check(!scenario.Steps[0].Body!.ContainsKey("userId"), "发送时填入当前账号，不修改共享场景");
        var rejected = false;
        try
        {
            await client.Send(new() { Operation = "profile-save", Body = JsonNode.Parse("""{"userId":2}""")!.AsObject() });
        }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && client.LastHttpStatus == null, "拒绝向当前目标发送其他账号的资料 ID");
    }
}
