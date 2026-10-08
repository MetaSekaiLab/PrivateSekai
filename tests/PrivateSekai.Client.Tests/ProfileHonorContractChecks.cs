extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;

internal static class ProfileHonorContractChecks
{
    public static void Run(Action<bool, string> check)
    {
        var definition = Operations.All["profile-honor-save"];
        var body = JsonNode.Parse("""
            {"profileHonors":[{"seq":3,"profileHonorType":"normal","honorId":1,"honorLevel":1,
              "bondsHonorViewType":"none","bondsHonorWordId":0,"honorBackgroundId":10101,"honorWordId":null}]}
            """)!.AsObject();
        var request = DumpSerializer.Deserialize<PutUserProfileHonorRequest>(Operations.EncodeBody(definition, body)!);
        var honor = request.profileHonors.Single();
        check(definition.Method == "PUT" && Operations.Path(definition, new(), 1) == "/api/user/1/profile-honor" &&
            definition.RequiredResponseField == "updatedResources", "称号设置使用客户端路由与资源响应键");
        check(honor.seq == 3 && honor.honorId == 1 && honor.honorLevel == 1 && honor.profileHonorType == "normal" &&
            honor.bondsHonorViewType == "none" && honor.honorBackgroundId == 10101 && honor.honorWordId == null,
            "称号请求保留槽位、等级及可空背景文字字段");
        var left = JsonNode.Parse("""{"userProfileHonors":[{"seq":1,"honorId":1},{"seq":3,"honorId":21}]}""");
        var right = JsonNode.Parse("""{"userProfileHonors":[{"seq":3,"honorId":21},{"seq":1,"honorId":1}]}""");
        check(Comparison.Diff(Comparison.Normalize(left), Comparison.Normalize(right)).Count == 0,
            "称号对比按槽位对齐而非数组位置");
        right!["userProfileHonors"]![0]!["seq"] = 2;
        check(Comparison.Diff(Comparison.Normalize(left), Comparison.Normalize(right)).Count != 0,
            "称号对比保留槽位变动");
    }
}
