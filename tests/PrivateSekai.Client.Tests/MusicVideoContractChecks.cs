extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;

internal static class MusicVideoContractChecks
{
    public static void Run(Action<bool, string> check)
    {
        var definition = Operations.All["music-video"];
        var request = DumpSerializer.Deserialize<UserMusicVideoRequest>(Operations.EncodeBody(definition,
            JsonNode.Parse("""{"musicVocal":3,"musicPlayStatus":"end","musicCategoryName":"mv_2d"}""")!.AsObject())!);
        check(definition.Method == "POST" && Operations.Path(definition, new() { Args = new() { ["musicId"] = "2" } }, 1)
            == "/api/user/1/music-video/2", "MV 使用 POST 和歌曲路径参数");
        check(request.musicVocal == 3 && request.musicPlayStatus == "end" && request.musicCategoryName == "mv_2d",
            "MV 请求使用 musicVocal、musicPlayStatus 和 musicCategoryName");
        check(definition.RequiredResponseField == "updatedResources" && definition.Snapshot && definition.IsWrite,
            "MV 检查资源响应并保留写前写后快照");
    }
}
