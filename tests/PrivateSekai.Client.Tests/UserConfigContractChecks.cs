extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;

internal static class UserConfigContractChecks
{
    public static void Run(Action<bool, string> check)
    {
        var definition = Operations.All["user-config-save"];
        var absent = DumpSerializer.Deserialize<PostUserConfigRequest>(Operations.EncodeBody(definition, new JsonObject())!);
        var disabled = DumpSerializer.Deserialize<PostUserConfigRequest>(Operations.EncodeBody(definition,
            new JsonObject { ["isDisplayLoginStatus"] = false })!);
        check(absent.isDisplayLoginStatus == null && disabled.isDisplayLoginStatus == false,
            "用户配置保留未指定和 false 的区别");
        check(absent.defaultMusicType == null && absent.friendRequestScope == null,
            "未指定的字符串配置保持 null");
        check(definition.Method == "POST" && Operations.Path(definition, new(), 1) == "/api/user/1/config" &&
            definition.IsWrite && definition.Snapshot && definition.RequiredResponseField == "updatedResources",
            "用户配置使用 POST 并采集前后快照");
    }
}
