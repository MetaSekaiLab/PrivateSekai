extern alias game;

using System.Net;
using System.Text.Json.Nodes;
using game::Sekai;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrivateSekai;
using PrivateSekai.Config;
using PrivateSekai.Client;
using PrivateSekai.Modules.Accounts;
using PrivateSekai.Modules.Cards;
using PrivateSekai.Modules.Gacha;
using PrivateSekai.Modules.Home;
using PrivateSekai.Modules.Live;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Modules.Presents;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Modules.Shop;
using PrivateSekai.Modules.Story;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;
using PrivateSekai.Transport;

var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    count++;
}
void Fails(Action action, string name)
{
    try { action(); } catch (InvalidOperationException) { count++; return; }
    throw new InvalidOperationException(name);
}
var networkFailure = FailureDiagnostics.Transport(new HttpRequestException(HttpRequestError.SecureConnectionError,
    "sensitive-diagnostic-placeholder", new IOException("sensitive-diagnostic-placeholder")))!;
Check(networkFailure["httpRequestError"]!.GetValue<string>() == "SecureConnectionError" &&
    networkFailure["innerErrorType"]!.GetValue<string>() == nameof(IOException) &&
    !networkFailure.ToJsonString().Contains("sensitive-diagnostic-placeholder"),
    "网络诊断仅记录固定分类和错误码，不记录异常正文");
var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/fixtures", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(directory);
CardHttpChecks.WriteMaster(directory);
ShopHttpChecks.WriteMaster(directory);
GachaHttpChecks.WriteMaster(directory);
LiveHttpChecks.WriteMaster(directory);
MissionHttpChecks.WriteMaster(directory);
StoryHttpChecks.WriteMaster(directory);
BookmarkHttpChecks.WriteMaster(directory);
FavoriteHttpChecks.WriteMaster(directory);
AccountReadHttpChecks.WriteTemplates(directory);
if (args is ["--replay-favorites", _, var favoriteMaster, _])
    FavoriteReplay.ImportMaster(favoriteMaster, directory);
if (args is ["--replay-story", _, var storyMaster, _])
    StoryReplay.ImportMaster(storyMaster, directory);
if (args is ["--replay-area-shop", _, var areaMaster, _])
    AreaShopReplay.ImportMaster(areaMaster, directory);
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
var settings = new Dictionary<string, string?>
{
    ["PrivateSekai:AesKey"] = new('k', 16), ["PrivateSekai:AesIv"] = new('i', 16),
    ["PrivateSekai:JwtKey"] = new('j', 32), ["PrivateSekai:EmptyRequestCiphertext"] = "00",
    ["PrivateSekai:IgnoreInvalidCredential"] = "false", ["PrivateSekai:SkipTutorial"] = "false",
    ["PrivateSekai:Debug"] = "false", ["PrivateSekai:Port"] = "0", ["PrivateSekai:GameVersionDomain"] = "example.invalid",
    ["PrivateSekai:Paths:Template"] = directory, ["PrivateSekai:Paths:SuiteMasterFile"] = directory,
    ["PrivateSekai:Paths:SekaiMasterDbDiff"] = directory
};
ServerConfig.Load(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), directory);
var store = new MemoryUserStore();
store.Save(1, new UserState { Data = new SuiteUser
{
    userRegistration = new() { userId = 1 }, userGamedata = new() { userId = 1, deck = 1 },
    userCards = [new() { cardId = 1 }, new() { cardId = 2 }], userDecks = [new() { deckId = 1, member1 = 1, leader = 1 }],
    userMaterials = [], userPresents = [new() { presentId = "fixture-present", resourceType = "material", resourceId = 1, resourceQuantity = 7 }], refreshableTypes = []
} });
builder.Services.AddPrivateSekai().AddSingleton<IUserStore>(store)
    .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory));
if (args is ["--replay-present", var replayCapture, _])
    builder.Services.AddSingleton<TimeProvider>(PresentReplay.Clock(replayCapture));
if (args is ["--replay-story", var storyCapture, _, _])
    builder.Services.AddSingleton<TimeProvider>(StoryReplay.Clock(storyCapture));
if (args is ["--replay-area-shop", var areaCapture, _, _])
    builder.Services.AddSingleton<TimeProvider>(AreaShopReplay.Clock(areaCapture));
builder.Services.AddControllers().AddApplicationPart(typeof(DeckController).Assembly)
    .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new TestedControllers()));
await using var app = builder.Build();
var token = "";
var requests = 0;
var omitNext = false;
string? noContentPath = null;
var validHeaders = true;
var imageRequests = 0;
var privateImageHeaders = false;
var shopRequests = new List<(string Method, long? Length)>();
var deckRequestUsers = new List<long>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/image"))
    {
        imageRequests++;
        privateImageHeaders |= context.Request.Headers.ContainsKey("X-Session-Token") ||
            context.Request.Headers.ContainsKey("Cookie") || context.Request.Headers.ContainsKey("Authorization");
        await next();
        return;
    }
    requests++;
    if (context.Request.Path.Value?.Contains("/shop/", StringComparison.Ordinal) == true)
        shopRequests.Add((context.Request.Method, context.Request.ContentLength));
    validHeaders &= context.Request.Headers.Accept.ToString() == "application/octet-stream"
        && context.Request.ContentType == "application/octet-stream";
    if (context.Request.Path == "/api/system") token = "";
    if (token.Length > 0 && context.Request.Headers["X-Session-Token"] != token)
    {
        context.Response.StatusCode = 409;
        return;
    }
    token = Guid.NewGuid().ToString();
    if (!omitNext) context.Response.Headers["X-Session-Token"] = token;
    if (noContentPath != null && context.Request.Path == noContentPath)
    {
        context.Response.StatusCode = 204;
        return;
    }
    await next();
});
app.UseRouting();
app.UseMiddleware<PrskCryptoMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/api/user/1/deck")
    {
        using var body = new MemoryStream();
        await context.Request.Body.CopyToAsync(body);
        context.Request.Body.Position = 0;
        var request = DumpSerializer.Deserialize<PutUserDeckRequest>(body.ToArray());
        deckRequestUsers.AddRange((request.userDeckUpdates ?? []).Select(update => update.userDeck.userId));
    }
    await next();
});
// 使用真实 MVC 业务控制器；全量状态读取使用隔离测试用户。
app.MapGet("/api/system", () => Results.Bytes(PrskCrypto.EncryptAesCbc(DumpSerializer.Serialize(new Dictionary<string, object> { ["appVersions"] = Array.Empty<object>() }))));
app.MapControllers();
await app.StartAsync();
try
{
    JsonFiles.Write(Path.Combine(directory, "headers.json"), new Dictionary<string, string> { ["Accept"] = "application/octet-stream" });
    var config = new TargetConfiguration { BaseUrl = app.Urls.Single(), UserId = 1, RequireRotatingToken = true, HeadersFile = "headers.json" };
    using var client = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
    if (args is ["--replay-present", var capturePath, var replayOutput])
    {
        await PresentReplay.Run(client, store, capturePath, replayOutput);
        return;
    }
    if (args is ["--replay-favorites", var favoriteCaptures, _, var favoriteOutput])
    {
        await FavoriteReplay.Run(client, store, favoriteCaptures, favoriteOutput);
        return;
    }
    if (args is ["--replay-area-shop", var areaPath, _, var areaOutput])
    {
        await AreaShopReplay.Run(client, store, areaPath, areaOutput);
        return;
    }
    if (args is ["--replay-story", var storyPath, _, var storyOutput])
    {
        await StoryReplay.Run(client, store, storyPath, storyOutput);
        return;
    }
    if (args is ["--replay-decks", var deckCaptures, var deckOutput])
    {
        await DeckReplay.Run(client, store, deckCaptures, deckOutput);
        return;
    }
    var scenario = new Scenario { Steps =
    [
        new() { Operation = "system" },
        new() { Operation = "suite" },
        new() { Operation = "deck-save", Body = JsonNode.Parse("""
          {"userDeckUpdates":[{"isDeleted":false,"userDeck":{"deckId":2,"name":"测试","leader":2,"subLeader":1,"member1":1,"member2":2}}],"mainDeckId":2}
          """)!.AsObject(), Expect = new() { ["/updatedResources/userGamedata/deck"] = JsonValue.Create(2) } }
    ] };
    ScenarioRunner.Validate(scenario, [config], new HashSet<string>());
    await ScenarioRunner.Run(client, scenario, directory);
    Check(store.Read(1)!.Data.userGamedata.deck == 2, "真实 HTTP 加密编队写入");
    Check(deckRequestUsers.SequenceEqual(new long[] { 1 }) &&
        scenario.Steps[2].Body!["userDeckUpdates"]![0]!["userDeck"]!["userId"] == null,
        "编队发包自动绑定当前账号，并保留原始场景不变");
    var beforeInvalidDeck = requests;
    Fails(() => client.Send(new() { Operation = "deck-save", Body = JsonNode.Parse(
        """{"userDeckUpdates":[{"userDeck":{"deckId":2,"userId":2}}]}""")!.AsObject() }).GetAwaiter().GetResult(),
        "拒绝编队请求中与当前目标不一致的账号");
    Check(requests == beforeInvalidDeck, "错误账号编队在发包前拒绝，不消耗会话 token");
    Check(requests == 5, "写入前后各同步一次，无额外或重复请求");
    Check(validHeaders, "配置 Accept 不重复，GET 与写请求都携带二进制 Content-Type");
    var record = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "003.json")))!.AsObject();
    Check(record["status"]!.GetValue<string>() == "completed" && record["stateChanges"]!.AsArray().Count > 0, "记录状态变化");
    Check(!record.ToJsonString().Contains(token), "不留存轮换 token");
    Check(record["after"]!["userRegistration"]!["userId"]!.GetValue<string>() == "<redacted>", "账号字段脱敏");
    await DeckNameHttpChecks.Run(client, config, store, directory, Check);
    noContentPath = "/api/user/1/story/unit_story/episode/991";
    await ScenarioRunner.Run(client, new() { Steps = [new()
    {
        Operation = "story-read", Args = new() { ["storyType"] = "unit_story", ["episodeId"] = "991" }
    }] }, Path.Combine(directory, "story-no-content"));
    var noContentRecord = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "story-no-content/001.json")))!;
    Check(noContentRecord["httpStatus"]!.GetValue<int>() == 204 && noContentRecord["response"]!.AsObject().Count == 0 &&
        noContentRecord["status"]!.GetValue<string>() == "completed",
        "官方主线 204 空响应保存状态码且轮换 token 后能读取完整状态");
    var differentStatus = noContentRecord.DeepClone().AsObject();
    differentStatus["httpStatus"] = 200;
    Check(ScenarioRunner.Compare(noContentRecord.AsObject(), differentStatus)["httpStatusDifferences"]!.AsArray().Count == 1,
        "双端响应正文相同但 HTTP 状态不同时仍报告差异");
    noContentPath = "/api/user/1/story/special_story/episode/991";
    using (var unsupportedClient = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
    {
        await unsupportedClient.Send(new() { Operation = "system" });
        var rejected = false;
        try { await unsupportedClient.Send(new() { Operation = "story-read", Args = new() { ["storyType"] = "special_story", ["episodeId"] = "991" } }); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "未核验剧情类型的 204 不当作成功响应");
    }
    noContentPath = null;
    await client.Send(new() { Operation = "system" });
    var presents = new Scenario { Steps =
    [
        new() { Operation = "present-history", Expect = new() { ["/userPresentHistories"] = new JsonArray() } },
        new() { Operation = "present-receive", Body = JsonNode.Parse("""{"presentIds":["fixture-present"]}""")!.AsObject(),
            Expect = new() { ["/receivedUserPresents/0/presentId"] = JsonValue.Create("fixture-present") } },
        new() { Operation = "present-history", Expect = new() { ["/userPresentHistories/0/presentId"] = JsonValue.Create("fixture-present") } },
        new() { Operation = "present-receive", Body = JsonNode.Parse("""{"presentIds":["fixture-present"]}""")!.AsObject(),
            Expect = new() { ["/receivedUserPresents"] = new JsonArray() } }
    ] };
    ScenarioRunner.Validate(presents, [config], new HashSet<string>());
    await ScenarioRunner.Run(client, presents, Path.Combine(directory, "presents"));
    var received = store.Read(1)!;
    Check(received.Data.userPresents.Count == 0 && received.Data.userMaterials.Single().quantity == 7,
        "真实 HTTP 领取移除礼物、发放材料且重复领取不重复发奖");
    Check(received.Private.PresentHistories.Count == 1, "真实 HTTP 领取历史仅增加一次");
    var presentRecord = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "presents/002.json")))!;
    Check(presentRecord["status"]!.GetValue<string>() == "completed" && presentRecord["stateChanges"]!.AsArray().Count > 0,
        "邮箱领取记录前后状态与变化");
    var receiptState = store.Read(1)!;
    receiptState.Data.userPresents = [new() { presentId = "fixture-practice", resourceType = "practice_ticket", resourceId = 2, resourceQuantity = 50 }];
    store.Save(1, receiptState);
    await ScenarioRunner.Run(client, new() { Steps = [new() { Operation = "present-receive",
        Body = JsonNode.Parse("""{"presentIds":["fixture-practice"]}""")!.AsObject() }] }, Path.Combine(directory, "practice-receipt"));
    var receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "practice-receipt/001.json")))!["response"]!["receivedUserPresents"]![0]!.AsObject();
    Check(!receipt.ContainsKey("resourceLevel") && receipt["receivedAt"]!.GetValue<long>() == store.Read(1)!.Private.PresentHistories.Last().receivedAt,
        "练习券领取响应按官方样本省略等级并返回与历史一致的领取时间");
    Check(receipt["seq"]!.GetValue<long>() == long.MaxValue - receipt["receivedAt"]!.GetValue<long>()
        && receipt["seq"]!.GetValue<long>() == store.Read(1)!.Private.PresentHistories.Last().seq
        && receipt["expiredAt"]!.GetValue<long>() - receipt["receivedAt"]!.GetValue<long>() == 30L * 24 * 60 * 60 * 1000,
        "练习券领取排序值、历史排序值及记录期限与官方两次样本一致");
    await CardHttpChecks.Run(client, store, directory, Check);
    await ShopHttpChecks.Run(client, store, directory, Check);
    Check(shopRequests.SequenceEqual(new (string, long?)[] { ("POST", 0), ("PUT", 0) }),
        "商店购买与升级分别使用 POST 和 PUT，均不发送 body");
    await GachaHttpChecks.Run(client, store, directory, Check);
    await LiveHttpChecks.Run(client, config, store, directory, Check);
    await HomeHttpChecks.Run(client, store, directory, Check);
    await MissionHttpChecks.Run(client, store, directory, Check);
    await StoryHttpChecks.Run(client, store, directory, Check);
    await BookmarkHttpChecks.Run(client, store, directory, Check);
    await FavoriteHttpChecks.Run(client, store, directory, Check);
    await ProfileHttpChecks.Run(client, store, directory, Check);
    await CustomProfileHttpChecks.Run(client, store, directory, Check);
    Check(imageRequests == 2 && !privateImageHeaders, "图片请求不发送 API 凭证且不消耗轮换 token");
    Fails(() => ThumbnailDownload.ValidatePath("https://example.invalid/image/custom-profile-card/thumbnail/a/b"), "图片拒绝外部 URL");
    Fails(() => ThumbnailDownload.ValidatePath("image/custom-profile-card/thumbnail/../b"), "图片拒绝路径穿越");
    await AccountReadHttpChecks.Run(client, store, directory, Check);
    await InheritHttpChecks.Run(client, config, store, directory, Check);
    Fails(() => Operations.Path(Operations.All["custom-profile-card-delete"], new() { Args = new() { ["customProfileId"] = "1" } }, 1),
        "名片删除必须提供卡片 ID 列表");
    Fails(() => ScenarioRunner.Validate(new() { Steps = [new() { Operation = "live-clear", UseLiveSession = true, Body = new() }] },
        [config], new HashSet<string>()), "未开局不能引用 Live ID");
    Fails(() => ScenarioRunner.Validate(new() { Steps =
        [new() { Operation = "live-start", Body = new() }, new() { Operation = "challenge-live-clear", UseLiveSession = true, Body = new() }]
    }, [config], new HashSet<string>()), "普通 Live 会话不能用于挑战结算");
    Fails(() => ScenarioRunner.Validate(new() { Steps =
        [new() { Operation = "challenge-live-start", Body = new() }, new() { Operation = "live-clear", UseLiveSession = true, Body = new() }]
    }, [config], new HashSet<string>()), "挑战 Live 会话不能用于普通结算");
    ScenarioRunner.Validate(new() { Steps =
        [new() { Operation = "challenge-live-start", Body = new() }, new() { Operation = "challenge-live-clear", UseLiveSession = true, Body = new() }]
    }, [config], new HashSet<string>());
    Check(Operations.Path(Operations.All["challenge-live-clear"],
        new() { Args = new() { ["userChallengeLiveId"] = "challenge-session" } }, 1) ==
        "/api/user/1/challenge-live/solo/challenge-session", "挑战结算支持独立字符串会话 ID");
    Fails(() => ScenarioRunner.Validate(new() { Steps =
    [new() { Operation = "live-start", Body = new() }, new() { Operation = "live-clear", UseLiveSession = true,
        Args = new() { ["userLiveId"] = "fixed-id" }, Body = new() }] }, [config], new HashSet<string>()),
        "自动引用与固定 Live ID 不能混用");
    Fails(() => Operations.Path(Operations.All["gacha-draw"], new()
    {
        Args = new() { ["gachaId"] = "1", ["gachaBehaviorId"] = "1" },
        Query = new() { ["isPriorityUsePaidJewel"] = "true&executeCount=10" }
    }, 1), "拒绝抽卡查询参数注入");
    Fails(() => Operations.Path(Operations.All["system"], new() { Query = new() { ["unexpected"] = "true" } }, 1),
        "拒绝操作未声明的查询参数");
    omitNext = true;
    var failed = false;
    try { await client.Suite(); } catch (InvalidOperationException) { failed = true; }
    Check(failed && client.LastResponse != null, "缺失下一枚 token 停止但保留脱敏响应");
    var prior = requests;
    try { await client.Suite(); } catch (InvalidOperationException) { }
    Check(prior == requests, "会话状态不明不继续发包");

    var left = JsonNode.Parse("""{"userMaterials":[{"materialId":1,"quantity":100},{"materialId":2,"quantity":7}],"now":1}""");
    var reordered = JsonNode.Parse("""{"userMaterials":[{"materialId":2,"quantity":7},{"materialId":1,"quantity":100}],"now":2}""");
    Check(Comparison.Diff(Comparison.Normalize(left), Comparison.Normalize(reordered)).Count == 0, "数组按业务 ID 对齐并忽略 now");
    var changes = Comparison.Diff(JsonNode.Parse("""{"quantity":100}"""), JsonNode.Parse("""{"quantity":90}"""));
    var other = Comparison.Diff(JsonNode.Parse("""{"quantity":30}"""), JsonNode.Parse("""{"quantity":20}"""));
    Check(Comparison.Diff(Comparison.DeltaView(changes), Comparison.DeltaView(other)).Count == 0, "初始库存不同但消耗一致");
    Check(Comparison.Diff(JsonNode.Parse("{}"), JsonNode.Parse("""{"x":null}""")).Single().Kind == "added", "区分缺失与 null");
    var redactor = new Redactor();
    redactor.AddSecret("fixture-secret");
    var cleaned = redactor.Clean(JsonNode.Parse("""{"sessionToken":"fixture-secret","nested":{"detail":"contains fixture-secret","deviceId":"private"}}"""))!.ToJsonString();
    Check(!cleaned.Contains("fixture-secret") && !cleaned.Contains("private"), "凭证字段与已知秘密嵌套脱敏");
    Fails(() => new TargetConfiguration { BaseUrl = "https://example.invalid" }.Validate(), "不能将远端伪装为 local");
    Fails(() => ScenarioRunner.Validate(scenario, [new() { Kind = "official", BaseUrl = "https://example.invalid" }], new HashSet<string>()), "官方写入需显式列出操作");
    Fails(() => Operations.Path(Operations.All["favorite-delete"], new() { Args = new() { ["shareNo"] = "../auth" } }, 1), "拒绝路径注入");
    Console.WriteLine($"MockClient：通过 {count} 项检查；隔离 HTTP 服务即将关闭。");
}
finally
{
    await app.StopAsync();
    Directory.Delete(directory, recursive: true);
}

sealed class TestedControllers : Microsoft.AspNetCore.Mvc.ApplicationParts.IApplicationFeatureProvider<Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature>
{
    public void PopulateFeature(IEnumerable<Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPart> parts,
        Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature feature)
    {
        Type[] tested = [typeof(DeckController), typeof(PresentController), typeof(CardController), typeof(ShopController), typeof(GachaController), typeof(LiveController), typeof(HomeController), typeof(MiscController), typeof(MissionController), typeof(ProfileController), typeof(CustomProfileController), typeof(LoginController), typeof(InheritController), typeof(StoryController), typeof(StoryBookmarkController), typeof(StoryFavoriteController)];
        foreach (var controller in feature.Controllers.Where(c => !tested.Contains(c.AsType())).ToArray())
            feature.Controllers.Remove(controller);
    }
}
