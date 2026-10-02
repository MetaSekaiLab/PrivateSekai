using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class BookmarkHttpChecks
{
    public static void WriteMaster(string directory) => File.WriteAllText(Path.Combine(directory, "configs.json"),
        """[{"configKey":"story_episode_bookmark_count","value":"20"},{"configKey":"story_episode_bookmark_name_max_length","value":"20"}]""");

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        // 仅用于协议和字节存储测试，不是真实截图。
        var scenario = new Scenario { Steps =
        [
            Step("bookmark-add", """{"name":"测试书签","bookmarkNameEditStatus":"edit","thumbnail":"/9j/2Q=="}"""),
            Step("bookmark-click"),
            Step("bookmark-rename", """{"name":"新名称","bookmarkNameEditStatus":"edit"}"""),
            Step("bookmark-list"),
            new() { Operation = "thumbnail-download", ThumbnailPathPointer = "/userStoryEpisodeBookmarks/0/thumbnailPath" }
        ] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        var output = Path.Combine(directory, "bookmarks");
        await ScenarioRunner.Run(client, scenario, output);
        var saved = store.Read(1)!;
        check(File.ReadAllBytes(Path.Combine(output, "005.jpg")).SequenceEqual(new byte[] { 255, 216, 255, 217 }),
            "按真实响应路径下载书签原始图片字节");
        var bookmark = saved.Private.StoryBookmarks["event_story"].Single();
        check(bookmark.ClickCount == 1 && bookmark.Bookmark.name == "新名称",
            "书签 HTTP 点击计数与名称修改持久化");
        check(saved.Data.userBookmarkedStories.Single().storyId == 1 && bookmark.Thumbnail.SequenceEqual(new byte[] { 255, 216, 255, 217 }),
            "书签创建同步剧情汇总并保存图片字节");
        var click = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "002.json")))!;
        check(click["response"] is JsonObject response && response.Count == 0 && click["status"]!.GetValue<string>() == "completed",
            "空 MessagePack map 响应完成请求并继续使用下一枚 token");
        var renamed = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "003.json")))!;
        check(renamed["response"]!["name"]!.GetValue<string>() == "<redacted>", "书签名称不写入报告");
        await ScenarioRunner.Run(client, new() { Steps = [Step("bookmark-delete"), Step("bookmark-list")] }, Path.Combine(output, "delete"));
        check(store.Read(1)!.Private.StoryBookmarks["event_story"].Count == 0 && store.Read(1)!.Data.userBookmarkedStories.Length == 0,
            "书签删除移除记录及剧情汇总");
    }

    private static ScenarioStep Step(string operation, string? body = null)
    {
        var args = new Dictionary<string, string> { ["storyType"] = "event_story", ["storyId"] = "1" };
        if (operation != "bookmark-list") { args["episodeId"] = "10"; args["talkId"] = "0"; }
        return new() { Operation = operation, Args = args, Body = body == null ? null : JsonNode.Parse(body)!.AsObject() };
    }
}
