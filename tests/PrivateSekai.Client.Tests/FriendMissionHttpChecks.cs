using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class FriendMissionHttpChecks
{
    public static void WriteMaster(string directory)
    {
        var path = Path.Combine(directory, "beginnerMissionV2s.json");
        var rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        rows.Add(JsonNode.Parse("""{"id":96,"beginnerMissionV2Type":"make_new_friend","requirement":3}"""));
        JsonFiles.Write(path, rows);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userFriends = [new() { opponentUserId = 901, friendStatus = "friend" },
            new() { opponentUserId = 902, friendStatus = "pending_request" }];
        state.Data.userBeginnerMissionV2s = [];
        state.Data.userMissionStatuses = [];
        store.Save(1, state);
        var first = await client.Suite();
        check(first["userBeginnerMissionV2s"]!.AsArray().Single()!["progress"]!.GetValue<int>() == 1 &&
            store.Read(1)!.Data.userBeginnerMissionV2s.Single().progress == 1,
            "完整Suite响应和持久状态按已确认好友数累加新手任务");
        await client.Send(new() { Operation = "suite-friends" });
        check(store.Read(1)!.Data.userBeginnerMissionV2s.Single().progress == 1,
            "好友parts读取不累计好友新手任务");
        state = store.Read(1)!;
        state.Data.userFriends[1].friendStatus = "friend";
        store.Save(1, state);
        var second = await client.Suite();
        check(second["userBeginnerMissionV2s"]!.AsArray().Single()!["progress"]!.GetValue<int>() == 3 &&
            store.Read(1)!.Data.userMissionStatuses.Single().missionId == 96,
            "好友数变化后Suite按新数量累加并达成master门槛");
        state = store.Read(1)!;
        state.Data.userFriends = [];
        store.Save(1, state);
        await client.Suite();
        check(store.Read(1)!.Data.userBeginnerMissionV2s.Single().progress == 3,
            "好友清空后Suite保留累计记录且不继续增加");
        store.Save(1, original);
    }
}
