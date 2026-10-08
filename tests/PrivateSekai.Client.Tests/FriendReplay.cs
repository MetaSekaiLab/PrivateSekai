extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class FriendReplay
{
    private static readonly string[] Fields = ["userGamedata", "userConfig", "userProfile", "userCards", "userDecks",
        "userProfileHonors", "userHonorMissions", "userPlayerFrames", "userFriends", "userMysekaiVisitSetting"];

    public static TimeProvider Clock(string manifest)
    {
        var input = Load(manifest);
        var write = Load(input["write"]!.GetValue<string>());
        return new SampleClock(write["response"]?["updatedResources"]?["now"]?.GetValue<long>() ?? write["before"]!["now"]!.GetValue<long>());
    }

    public static async Task Run(ProtocolClient caller, ProtocolClient readback, ProtocolClient peer, MemoryUserStore store, string manifest, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("重放输出目录必须为空。");
        var input = Load(manifest);
        var official = Load(input["write"]!.GetValue<string>());
        var peerBefore = Load(input["peerBefore"]!.GetValue<string>())["response"]!;
        var peerAfter = Load(input["peerAfter"]!.GetValue<string>())["response"]!;
        var operation = official["operation"]!.GetValue<string>();
        if (operation is not ("friend-request" or "friend-approve" or "friend-cancel" or "friend-reject" or "friend-release" or "profile-save"))
            throw new InvalidOperationException("需要好友写入样本。");
        var rejected = official["status"]?.GetValue<string>() == "stopped";
        var expectedStatus = (rejected ? official["lastHttpStatus"] : official["httpStatus"])!.GetValue<int>();
        if (rejected && (official["failurePhase"]?.GetValue<string>() != "request" || expectedStatus is not (400 or 404 or 409)))
            throw new InvalidOperationException("需要成功或已收到明确拒绝的记录。");
        long Map(long id) => id == input["callerId"]!.GetValue<long>() ? 1 : id == input["peerId"]!.GetValue<long>() ? 2 : id;
        void Remap(JsonNode? node, long owner)
        {
            if (node is JsonArray array) foreach (var child in array) Remap(child, owner);
            if (node is not JsonObject obj) return;
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                if (key == "userId") obj[key] = owner;
                else if (key == "opponentUserId") obj[key] = Map(long.Parse(obj[key]!.ToString(), System.Globalization.CultureInfo.InvariantCulture));
                else Remap(obj[key], owner);
            }
        }
        foreach (var pair in new[] { (Id: 1L, Data: official["before"]!), (Id: 2L, Data: peerBefore) })
        {
            var baseline = pair.Data.DeepClone();
            Remap(baseline, pair.Id);
            var state = store.Read(1)!;
            state.Data.userRegistration!.userId = pair.Id;
            foreach (var member in DumpContract.For(typeof(SuiteUser)).Members.Where(m => Fields.Contains((string)m.Key)))
                member.Set(state.Data, baseline[(string)member.Key] is { } value
                    ? JsonSerializer.Deserialize(value.ToJsonString(), member.Type, DumpJson.Options) : null);
            store.Save(pair.Id, state);
        }
        var expectedFriends = (rejected ? official["before"] : official["response"]!["updatedResources"])!.DeepClone();
        Remap(expectedFriends, 1);
        var status = expectedFriends["userFriends"]?.AsArray().SingleOrDefault(f => f!["opponentUserId"]!.GetValue<long>() == 2)?["userLoginStatus"];
        if (status != null)
        {
            var state = store.Read(2)!;
            state.Private.LoginStatus = JsonSerializer.Deserialize<UserLoginStatus>(status.ToJsonString(), DumpJson.Options);
            store.Save(2, state);
        }
        await caller.Send(new() { Operation = "system" });
        var body = official["request"]?.DeepClone().AsObject();
        body?.Remove("userId");
        try
        {
            await ScenarioRunner.Run(caller, new() { Steps = [new()
            {
                Operation = operation, Args = operation == "profile-save" ? new() : new() { ["opponentUserId"] = "2" },
                Body = body
            }] }, output);
        }
        catch (ClientFailure) when (rejected && caller.LastHttpStatus == expectedStatus) { }
        var local = Load(Path.Combine(output, "001.json"));
        var actualResponse = rejected ? local["lastResponse"] : local["response"]!["updatedResources"];
        var expectedResponse = rejected ? official["lastResponse"] : expectedFriends;
        JsonNode? Clean(JsonNode? source, bool includeStatus)
        {
            var result = caller.Redactor.Clean(source);
            if (result is not JsonObject obj || obj.ContainsKey("httpStatus") || obj.ContainsKey("format")) return result;
            var selected = new JsonObject();
            if (operation == "profile-save" && obj.ContainsKey("userProfile"))
                selected["userProfile"] = obj["userProfile"]!.DeepClone();
            if (obj.ContainsKey("userFriends"))
            {
                var friends = obj["userFriends"]!.DeepClone();
                if (!includeStatus) foreach (var friend in friends.AsArray()) friend!.AsObject().Remove("userLoginStatus");
                selected["userFriends"] = friends;
            }
            return selected;
        }
        var report = new JsonObject
        {
            ["httpStatusMatches"] = (rejected ? local["lastHttpStatus"] : local["httpStatus"])!.GetValue<int>() == expectedStatus,
            ["responseDifferences"] = JsonSerializer.SerializeToNode(Comparison.Diff(Clean(expectedResponse, true), Clean(actualResponse, true)), JsonFiles.Options)
        };
        foreach (var item in new[] { (Name: "caller", Client: readback, Expected: rejected ? official["before"]! : official["after"]!, Id: 1L),
            (Name: "peer", Client: peer, Expected: peerAfter, Id: 2L) })
        {
            item.Client.CaptureTo(Path.Combine(output, item.Name));
            await item.Client.Send(new() { Operation = "system" });
            var actual = await item.Client.Send(new() { Operation = operation == "profile-save" && item.Id == 1 ? "suite" : "suite-friends" });
            var expected = item.Expected.DeepClone();
            Remap(expected, item.Id);
            report[item.Name + "Differences"] = JsonSerializer.SerializeToNode(Comparison.Diff(Clean(expected, false), Clean(actual, false)), JsonFiles.Options);
        }
        JsonFiles.Write(Path.Combine(output, "friend-compare.json"), report);
        if (!report["httpStatusMatches"]!.GetValue<bool>() || report.Where(p => p.Key.EndsWith("Differences", StringComparison.Ordinal)).Any(p => p.Value!.AsArray().Count != 0))
            throw new InvalidOperationException("好友 HTTP 响应或双方回读存在差异，见报告。");
        Console.WriteLine("好友 HTTP 响应及双方关系回读对拍通过；回读不比较会话改变的在线状态。");
    }

    private static JsonObject Load(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    private sealed class SampleClock(long timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
    }
}
