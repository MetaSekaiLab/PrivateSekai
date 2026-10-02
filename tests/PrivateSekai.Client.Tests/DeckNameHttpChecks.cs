using System.Text.Json.Nodes;
using PrivateSekai.Config;
using PrivateSekai.Client;
using PrivateSekai.Modules.Live;
using PrivateSekai.Storage;

internal static class DeckNameHttpChecks
{
    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        foreach (var name in new[] { "abcdefghij", "あいうえおかきくけこ" })
        {
            await client.Send(Step(name));
            check(store.Read(1)!.Data.userDecks.Single(d => d.deckId == 1).name == name,
                "官方已接受的十字符 ASCII 与日文编队名称可保存");
        }
        check(!DeckService.IsNameTooLong(string.Concat(Enumerable.Repeat("😀", 5))),
            "五个补充平面字符仅在长度上不超过十个 UTF-16 单元");
        var index = 0;
        foreach (var name in new[] { "abcdefghijk", string.Concat(Enumerable.Repeat("😀", 6)) })
        {
            using var rejected = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await rejected.Send(new() { Operation = "system" });
            var output = Path.Combine(directory, "deck-name-" + index++);
            var failed = false;
            try { await ScenarioRunner.Run(rejected, new() { Steps = [Step(name)] }, output); }
            catch (ClientFailure) { failed = true; }
            check(failed && rejected.LastHttpStatus == 400 && JsonNode.DeepEquals(rejected.LastResponse,
                JsonNode.Parse("""{"userDeckUpdates[0].userDeck.name":"Length"}""")),
                "过长名称按官方结构返回加密 HTTP 400 和 Length 字段错误");
            var saved = store.Read(1)!.Data;
            check(saved.userDecks.Single(d => d.deckId == 1).name == "あいうえおかきくけこ" && saved.userGamedata.deck == 2,
                "名称失败不修改原编队或主编队");
        }
        await client.Send(new() { Operation = "system" });
    }

    private static ScenarioStep Step(string name) => new()
    {
        Operation = "deck-save", Body = new JsonObject
        {
            ["userDeckUpdates"] = new JsonArray(new JsonObject
            {
                ["isDeleted"] = false, ["userDeck"] = new JsonObject
                {
                    ["deckId"] = 1, ["name"] = name, ["leader"] = 1,
                    ["member1"] = 1, ["member2"] = 2
                }
            })
        }
    };
}
