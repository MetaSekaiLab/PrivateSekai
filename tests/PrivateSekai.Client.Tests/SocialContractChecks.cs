extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;

internal static class SocialContractChecks
{
    public static void Run(Action<bool, string> check)
    {
        var status = Operations.All["login-status-save"];
        check(status.Method == "PUT" && status.RequiredResponseField == null && !status.Snapshot && status.IsWrite,
            "在线状态接受空 map，避免自身 Suite 快照覆盖状态");
        var request = DumpSerializer.Deserialize<PutUserLoginStatusRequest>(Operations.EncodeBody(status,
            new JsonObject { ["loginStatus"] = "solo_live" })!);
        check(request.loginStatus == "solo_live", "在线状态按字符串键编码");
        var friend = Operations.All["friend-request"];
        check(Operations.Path(friend, new() { Args = new() { ["opponentUserId"] = "400000000000000001" } }, 1)
            == "/api/user/1/friend/400000000000000001", "好友路径支持正 64 位账号 ID");
        var body = DumpSerializer.Deserialize<PostUserFriendRequest>(Operations.EncodeBody(friend,
            new JsonObject { ["message"] = "", ["friendRequestSentLocation"] = "id_search" })!);
        check(body.sentLocation == "id_search" && body.message == "", "好友申请使用协议键 friendRequestSentLocation");
        foreach (var (name, type) in new[] { ("friend-cancel", "cancel_friend_request"),
            ("friend-reject", "reject_friend_request"), ("friend-release", "release_friend") })
        {
            var operation = Operations.All[name];
            check(operation.Method == "DELETE" && operation.RequestType == null && operation.RequiredResponseField == "updatedResources" &&
                Operations.Path(operation, new() { Args = new() { ["opponentUserId"] = "400000000000000001" } }, 1) ==
                "/api/user/1/friend/400000000000000001?type=" + type, "好友删除动作的类型、方法与路径契约");
        }
    }
}
