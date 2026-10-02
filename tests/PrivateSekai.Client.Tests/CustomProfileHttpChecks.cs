using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class CustomProfileHttpChecks
{
    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userCustomProfiles = [];
        state.Data.userCustomProfileCards = [];
        state.Data.userCustomProfileResourceUsages = [];
        store.Save(1, state);
        var scenario = new Scenario { Steps =
        [
            new() { Operation = "custom-profile-save", Args = new() { ["customProfileId"] = "1" }, Body = new() { ["name"] = "fixture-profile" } },
            Card("custom-profile-card-create", "1", 7),
            Card("custom-profile-card-create", "2", 8),
            Card("custom-profile-card-update", "1", 8),
            new() { Operation = "custom-profile-save", Args = new() { ["customProfileId"] = "1" },
                Body = JsonNode.Parse("""{"name":"fixture-renamed","customProfileCardOrders":[{"customProfileId":1,"customProfileCardId":1,"seq":2},{"customProfileId":1,"customProfileCardId":2,"seq":1}]}""")!.AsObject() },
            new() { Operation = "thumbnail-download", ThumbnailPathPointer = "/updatedResources/userCustomProfileCards/0/thumbnailPath" },
            new() { Operation = "custom-profile-card-delete", Args = new() { ["customProfileId"] = "1" },
                QueryLists = new() { ["customProfileCardId"] = [1, 2] } }
        ] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        await ScenarioRunner.Run(client, scenario, Path.Combine(directory, "custom-profile"));
        var updated = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "custom-profile/004.json")))!;
        check(updated["after"]!["userCustomProfileResourceUsages"]![0]!["quantity"]!.GetValue<int>() == 2,
            "名片卡片更新后资源引用数随内容变化");
        var reordered = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "custom-profile/005.json")))!;
        var cards = reordered["after"]!["userCustomProfileCards"]!.AsArray();
        check(cards.Single(c => c!["customProfileCardId"]!.GetValue<int>() == 1)!["seq"]!.GetValue<int>() == 2,
            "名片排序请求保存卡片顺序");
        var saved = store.Read(1)!.Data;
        var image = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "custom-profile/006.json")))!;
        check(File.ReadAllBytes(Path.Combine(directory, "custom-profile/006.png")).SequenceEqual(
                Convert.FromBase64String(scenario.Steps[1].Body!["thumbnail"]!.GetValue<string>())) &&
            image["response"]!["sha256"]!.GetValue<string>().Length == 64,
            "名片 PNG 下载保存原字节并记录摘要供两端比较");
        check(saved.userCustomProfileCards.Length == 0 && saved.userCustomProfileResourceUsages.Length == 0 &&
            saved.userCustomProfiles.Single().name == "fixture-renamed", "重复 query 删除两张卡片并清理引用，保留名片名称");
        var first = File.ReadAllText(Path.Combine(directory, "custom-profile/002.json"));
        check(!first.Contains("iVBORw0KGgo") && !first.Contains("fixture-profile-text"), "名片缩略图与文字不会进入记录");
        check(updated["after"]!["userCustomProfileCards"]![0]!["thumbnailPath"]!.GetValue<string>().Contains('/'),
            "名片上传缩略图后保存服务端图片引用");
    }

    private static ScenarioStep Card(string operation, string id, int collection) => new()
    {
        Operation = operation, Args = new() { ["customProfileId"] = "1", ["customProfileCardId"] = id },
        Body = new()
        {
            ["thumbnail"] = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9ZkAAAAASUVORK5CYII=",
            ["customProfileCard"] = new JsonObject
            {
                ["version"] = 4,
                ["collections"] = new JsonArray(new JsonObject { ["id"] = collection }),
                ["texts"] = new JsonArray(new JsonObject { ["text"] = "fixture-profile-text" })
            }
        }
    };
}
