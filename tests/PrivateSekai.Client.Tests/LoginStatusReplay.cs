extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class LoginStatusReplay
{
    public static TimeProvider Clock(string observerPath) => new SampleClock(Status(observerPath)["loginStatusUpdatedAt"]?.GetValue<long>() ?? 1700000000000);

    private static JsonObject Status(string path) => JsonNode.Parse(File.ReadAllText(path))!["response"]!["userFriends"]!.AsArray()
        .Single()!["userLoginStatus"]!.DeepClone().AsObject();

    public static async Task Run(ProtocolClient writer, ProtocolClient observer, MemoryUserStore store,
        string path, string observerPath, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("输出目录必须为空。");
        var official = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (official["operation"]?.GetValue<string>() != "login-status-save")
            throw new InvalidOperationException("需要在线状态样本。");
        var rejected = official["status"]?.GetValue<string>() == "stopped";
        if (rejected ? official["lastHttpStatus"]?.GetValue<int>() != 400 : official["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功或明确 400 的样本。");
        var expected = Status(observerPath);
        var state = store.Read(1)!;
        state.Data.userConfig = new UserConfig { isDisplayLoginStatus = expected.ContainsKey("loginStatusUpdatedAt") };
        state.Private.LoginStatus = new UserLoginStatus
        {
            loginStatus = rejected ? expected["loginStatus"]!.GetValue<string>() : "online",
            loginStatusUpdatedAt = rejected ? expected["loginStatusUpdatedAt"]!.GetValue<long>() : 1
        };
        store.Save(1, state);
        var viewer = store.Read(1)!;
        viewer.Data.userRegistration!.userId = 2;
        viewer.Data.userGamedata!.userId = 2;
        viewer.Data.userFriends = [new UserFriend { opponentUserId = 1, friendStatus = "friend" }];
        store.Save(2, viewer);
        await writer.Send(new() { Operation = "system" });
        try
        {
            await ScenarioRunner.Run(writer, new() { Steps = [new()
            {
                Operation = "login-status-save", Body = official["request"]!.DeepClone().AsObject()
            }] }, output);
        }
        catch (ClientFailure) when (rejected && writer.LastHttpStatus == 400) { }
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        var report = ScenarioRunner.Compare(official, local);
        observer.CaptureTo(Path.Combine(output, "observer"));
        await observer.Send(new() { Operation = "system" });
        var response = await observer.Send(new() { Operation = "suite-friends" });
        var actual = response["userFriends"]!.AsArray().Single()!["userLoginStatus"];
        var differences = Comparison.Diff(expected, actual);
        report["observerDifferences"] = System.Text.Json.JsonSerializer.SerializeToNode(differences, JsonFiles.Options);
        JsonFiles.Write(Path.Combine(output, "login-status-compare.json"), report);
        if (differences.Count != 0 || new[] { "httpStatusDifferences", "responseDifferences" }.Any(k => report[k]!.AsArray().Count != 0))
            throw new InvalidOperationException("在线状态响应或另一账号回读存在差异。");
        Console.WriteLine("在线状态响应及另一账号回读对拍通过。");
    }

    private sealed class SampleClock(long timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
    }
}
