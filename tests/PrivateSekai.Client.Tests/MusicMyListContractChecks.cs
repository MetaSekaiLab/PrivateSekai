extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;

internal static class MusicMyListContractChecks
{
    public static void Run(Action<bool, string> check)
    {
        var save = Operations.All["music-my-list-save"];
        var reset = Operations.All["music-my-list-reset"];
        var step = new ScenarioStep { Args = new() { ["listNo"] = "5" } };
        check(save.Method == "PUT" && reset.Method == "PATCH" &&
            Operations.Path(save, step, 1) == "/api/user/1/myList/5" &&
            Operations.Path(reset, step, 1) == "/api/user/1/myList/5",
            "My List 保存与重置使用客户端大小写路径和独立 HTTP method");
        var body = JsonNode.Parse("""{"name":"fixture","musicIds":[3,1,2]}""")!.AsObject();
        var request = DumpSerializer.Deserialize<PutUserMusicMyListRequest>(Operations.EncodeBody(save, body)!);
        check(request.name == "fixture" && request.musicIds.SequenceEqual(new[] { 3, 1, 2 }) &&
            Operations.EncodeBody(reset, null) == null, "保存保留歌曲顺序，重置不发送请求体");
        foreach (var type in new[] { typeof(PutUserMusicMyListResponse), typeof(PatchUserMusicMyListResponse) })
            check(DumpContract.For(type).Members.Single().Key.Equals("updateResources") &&
                save.RequiredResponseField == "updateResources" && reset.RequiredResponseField == "updateResources",
                "My List 使用 dump 的 updateResources 响应键");
        var first = JsonNode.Parse("""{"userMyLists":[{"listNo":1,"musicIds":[3,1]},{"listNo":2,"musicIds":[]}]}""");
        var reordered = JsonNode.Parse("""{"userMyLists":[{"listNo":2,"musicIds":[]},{"listNo":1,"musicIds":[3,1]}]}""");
        check(Comparison.Diff(Comparison.Normalize(first), Comparison.Normalize(reordered)).Count == 0,
            "My List 对比按 listNo 匹配列表");
        reordered!["userMyLists"]![1]!["musicIds"] = new JsonArray(1, 3);
        check(Comparison.Diff(Comparison.Normalize(first), Comparison.Normalize(reordered)).Count != 0,
            "My List 对比保留列表内部歌曲顺序差异");
    }
}
