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
using PrivateSekai.Modules.Music;
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
MusicMyListContractChecks.Run(Check);
ProfileHonorContractChecks.Run(Check);
MusicVideoContractChecks.Run(Check);
UserConfigContractChecks.Run(Check);
SocialContractChecks.Run(Check);
LiveRewardAuditChecks.Run(Check);
DiarkisEncryptionChecks.Run(Check);
DiarkisPacketChecks.Run(Check);
DiarkisSplitPacketChecks.Run(Check);
DiarkisEchoChecks.Run(Check);
MultiLiveReservationChecks.Run(Check);
await DiarkisUdpChecks.Run(Check);
var networkFailure = FailureDiagnostics.Transport(new HttpRequestException(HttpRequestError.SecureConnectionError,
    "sensitive-diagnostic-placeholder", new IOException("sensitive-diagnostic-placeholder")))!;
Check(networkFailure["httpRequestError"]!.GetValue<string>() == "SecureConnectionError" &&
    networkFailure["innerErrorType"]!.GetValue<string>() == nameof(IOException) &&
    !networkFailure.ToJsonString().Contains("sensitive-diagnostic-placeholder"),
    "网络诊断仅记录固定分类和错误码，不记录异常正文");
var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../obj/fixtures", Guid.NewGuid().ToString("N")));
var rejectedSkill = JsonNode.Parse("""
    {"operation":"skill-material","status":"stopped","failurePhase":"request","lastHttpStatus":409,
     "lastResponse":{"errorCode":""},"before":{"quantity":300}}
    """)!.AsObject();
var acceptedSkill = JsonNode.Parse("""
    {"operation":"skill-material","status":"completed","httpStatus":200,
     "response":{"updatedResources":{}},"before":{"quantity":300},"after":{"quantity":299}}
    """)!.AsObject();
var rejectedComparison = ScenarioRunner.Compare(rejectedSkill, acceptedSkill);
Check(rejectedComparison["httpStatusDifferences"]!.AsArray().Count == 1 &&
    !rejectedComparison["complete"]!.GetValue<bool>(), "官方拒绝与本地成功的状态差异不能漏报");
Check(rejectedComparison["deltaDifferences"] == null, "缺少回读时不把全部状态误报为删除");
var otherRejection = rejectedSkill.DeepClone().AsObject();
otherRejection["lastResponse"]!["errorCode"] = "fixture-error";
Check(ScenarioRunner.Compare(rejectedSkill, otherRejection)["responseDifferences"]!.AsArray().Count == 1,
    "明确请求失败时比较实际错误响应");
var failedReadback = acceptedSkill.DeepClone().AsObject();
failedReadback["status"] = "stopped";
failedReadback["failurePhase"] = "after-snapshot";
failedReadback["lastHttpStatus"] = 503;
Check(ScenarioRunner.Compare(acceptedSkill, failedReadback)["httpStatusDifferences"]!.AsArray().Count == 0,
    "回读失败的状态码不能覆盖写操作的成功状态码");
Directory.CreateDirectory(directory);
CardHttpChecks.WriteMaster(directory);
ShopHttpChecks.WriteMaster(directory);
GachaHttpChecks.WriteMaster(directory);
LiveHttpChecks.WriteMaster(directory);
MissionHttpChecks.WriteMaster(directory);
CostumeHttpChecks.WriteMaster(directory);
StoryHttpChecks.WriteMaster(directory);
BookmarkHttpChecks.WriteMaster(directory);
ProfileHttpChecks.WriteMaster(directory);
FavoriteHttpChecks.WriteMaster(directory);
ChallengeDeckHttpChecks.WriteMaster(directory);
MaterialExchangeHttpChecks.WriteMaster(directory);
EventExchangeHttpChecks.WriteMaster(directory);
CostumeHttpChecks.WriteCraftMaster(directory);
CharacterMissionHttpChecks.WriteMaster(directory);
AccountReadHttpChecks.WriteTemplates(directory);
LiveHttpChecks.WriteBoostMaster(directory);
FriendMissionHttpChecks.WriteMaster(directory);
BeginnerCompletionHttpChecks.WriteMaster(directory);
if (args is ["--replay-character-mission", _, var characterMissionMaster, _])
    CharacterMissionReplay.ImportMaster(characterMissionMaster, directory);
if (args is ["--replay-costume" or "--replay-costume-craft", _, var costumeMaster, _])
    CostumeReplay.ImportMaster(costumeMaster, directory);
if (args is ["--replay-live-mission" or "--replay-beginner-mission" or "--replay-beginner-repeat", _, var missionMaster, _])
    LiveMissionReplay.ImportMaster(missionMaster, directory);
if (args is ["--replay-favorites", _, var favoriteMaster, _])
    FavoriteReplay.ImportMaster(favoriteMaster, directory);
if (args is ["--replay-story", _, var storyMaster, _])
    StoryReplay.ImportMaster(storyMaster, directory);
if (args is ["--replay-area-shop" or "--replay-music-shop" or "--replay-stamp-shop" or "--replay-vocal-shop" or "--replay-vocal-rejection" or "--replay-stamp-rejection", _, var areaMaster, _])
    AreaShopReplay.ImportMaster(areaMaster, directory);
if (args is ["--replay-card-practice" or "--replay-special-training" or "--replay-card-image", _, var practiceMaster, _])
    CardPracticeReplay.ImportMaster(practiceMaster, directory);
if (args is ["--replay-skill-practice", _, _, var skillMaster, _])
    CardPracticeReplay.ImportMaster(skillMaster, directory);
if (args is ["--replay-material-exchange" or "--replay-event-exchange", _, var exchangeMaster, _])
    MaterialExchangeReplay.ImportMaster(exchangeMaster, directory);
if (args is ["--replay-live" or "--replay-live-honor" or "--replay-live-result" or "--replay-live-boost" or "--replay-live-achievement" or "--replay-live-experience" or "--replay-live-point", _, _, var liveMaster, _])
    LiveReplay.ImportMaster(liveMaster, directory);
if (args is ["--replay-boost-item", _, var boostMaster, _])
    BoostReplay.ImportMaster(boostMaster, directory);
if (args is ["--replay-natural-boost", _, _, var naturalBoostMaster, _])
    BoostReplay.ImportMaster(naturalBoostMaster, directory);
if (args is ["--replay-challenge-deck" or "--replay-challenge-unlock" or "--replay-challenge-start", _, var challengeMaster, _])
    ChallengeDeckReplay.ImportMaster(challengeMaster, directory);
if (args is ["--replay-login-bonus", _, var loginMaster, _])
    LoginBonusReplay.ImportMaster(loginMaster, directory);
if (args is ["--replay-challenge-exp", _, var challengeExpMaster, _])
    ChallengeExperienceReplay.ImportMaster(challengeExpMaster, directory);
if (args is ["--replay-challenge-stage", _, var stageMaster, _])
    ChallengeStageReplay.ImportMaster(stageMaster, directory);
if (args is ["--replay-challenge-play-day", _, var dayMaster, _])
    ChallengePlayDayReplay.ImportMaster(dayMaster, directory);
if (args is ["--replay-challenge-high-score", _, var highScoreMaster, _])
    ChallengeHighScoreReplay.ImportMaster(highScoreMaster, directory);
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
    .AddSingleton(_ => new PrivateSekai.Storage.CustomProfileThumbnailStore())
    .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory));
if (args is ["--replay-profile-honor" or "--replay-profile-honor-mission", _, var honorMaster, _])
    builder.Services.AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, honorMaster));
if (args is ["--replay-honor-mission", var honorMissionCapture, var honorMissionMaster, _])
{
    builder.Services.AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, honorMissionMaster));
    builder.Services.AddSingleton(HonorMissionReplay.Clock(honorMissionCapture));
}
if (args is ["--replay-music-video", _, var videoMaster, _])
    builder.Services.AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, videoMaster));
if (args is ["--replay-live-start-rejection", var startCapture, _, var startMaster, _])
{
    builder.Services.AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, startMaster));
    builder.Services.AddSingleton(LiveStartReplay.Clock(startCapture));
}
if (args is ["--replay-live-start", var acceptedStartCapture, var acceptedStartMaster, _])
{
    builder.Services.AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, acceptedStartMaster));
    builder.Services.AddSingleton(LiveStartReplay.Clock(acceptedStartCapture));
}
if (args is ["--replay-friend", var friendManifest, var friendMaster, _])
{
    builder.Services.AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, friendMaster));
    builder.Services.AddSingleton(FriendReplay.Clock(friendManifest));
}
if (args is ["--replay-character-mission", var characterMissionCapture, _, _])
    builder.Services.AddSingleton<TimeProvider>(ChallengeDeckReplay.Clock(characterMissionCapture));
if (args is ["--replay-costume-craft", var craftCapture, _, _])
    builder.Services.AddSingleton<TimeProvider>(ChallengeDeckReplay.Clock(craftCapture));
if (args is ["--replay-login-bonus", var loginCapture, _, _])
    builder.Services.AddSingleton<TimeProvider>(LoginBonusReplay.Clock(loginCapture));
if (args is ["--replay-present", var replayCapture, _])
    builder.Services.AddSingleton<TimeProvider>(PresentReplay.Clock(replayCapture));
if (args is ["--replay-story", var storyCapture, _, _])
    builder.Services.AddSingleton<TimeProvider>(StoryReplay.Clock(storyCapture));
if (args is ["--replay-area-shop" or "--replay-stamp-shop" or "--replay-vocal-shop", var areaCapture, _, _])
    builder.Services.AddSingleton<TimeProvider>(AreaShopReplay.Clock(areaCapture));
if (args is ["--replay-live" or "--replay-live-honor" or "--replay-live-result" or "--replay-live-boost" or "--replay-live-achievement" or "--replay-live-experience" or "--replay-live-point", _, var liveCapture, _, _])
    builder.Services.AddSingleton<TimeProvider>(LiveReplay.Clock(liveCapture));
if (args is ["--replay-boost-item", var boostClockCapture, _, _])
    builder.Services.AddSingleton<TimeProvider>(MaterialExchangeReplay.Clock(boostClockCapture));
if (args is ["--replay-natural-boost", _, var naturalBoostClock, _, _])
    builder.Services.AddSingleton<TimeProvider>(BoostReplay.NaturalClock(naturalBoostClock));
if (args is ["--replay-challenge-unlock" or "--replay-challenge-start", var challengeClockCapture, _, _])
    builder.Services.AddSingleton<TimeProvider>(ChallengeDeckReplay.Clock(challengeClockCapture));
if (args is ["--replay-challenge-play-day" or "--replay-challenge-stage", var dayCaptureClock, _, _])
    builder.Services.AddSingleton<TimeProvider>(ChallengeDeckReplay.Clock(dayCaptureClock));
if (args is ["--replay-material-exchange" or "--replay-event-exchange", var exchangeClockCapture, _, _])
    builder.Services.AddSingleton<TimeProvider>(MaterialExchangeReplay.Clock(exchangeClockCapture));
if (args is ["--replay-login-status", _, var observerClockPath, _])
    builder.Services.AddSingleton(LoginStatusReplay.Clock(observerClockPath));
builder.Services.AddControllers().AddApplicationPart(typeof(DeckController).Assembly)
    .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new TestedControllers()));
await using var app = builder.Build();
if (args is ["--replay-challenge-high-score", var highScoreCapture, _, var highScoreOutput])
{
    ChallengeHighScoreReplay.Run(app.Services, store, highScoreCapture, highScoreOutput);
    return;
}
if (args is ["--replay-challenge-play-day", var dayCapture, _, var dayOutput])
{
    ChallengePlayDayReplay.Run(app.Services, store, dayCapture, dayOutput);
    return;
}
if (args is ["--replay-challenge-exp", var challengeExpCapture, _, var challengeExpOutput])
{
    ChallengeExperienceReplay.Run(app.Services, store, challengeExpCapture, challengeExpOutput);
    return;
}
if (args is ["--replay-challenge-stage", var stageCapture, _, var stageOutput])
{
    ChallengeStageReplay.Run(app.Services, store, stageCapture, stageOutput);
    return;
}
var token = "";
var requests = 0;
int? failAtRequest = null;
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
    if (requests == failAtRequest)
    {
        context.Response.StatusCode = 503;
        return;
    }
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
    if (context.Request.Path.StartsWithSegments("/api/suitemasterfile"))
    {
        await next();
        return;
    }
    token = Guid.NewGuid().ToString();
    if (!omitNext) context.Response.Headers["X-Session-Token"] = token;
    if (context.Request.Path == "/api/system") context.Response.Headers["X-Login-Bonus-Status"] = "true";
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
app.MapGet("/api/user/1/diarkis-auth", (HttpContext context) =>
{
    Check(context.Request.Query["diarkisServerType"] == "multi" && context.Request.ContentLength is null or 0,
        "实时认证以GET查询指定服务器类型且无请求体");
    return Results.Bytes(PrskCrypto.EncryptAesCbc(DumpSerializer.Serialize(new Dictionary<string, object>
    {
        ["userId"] = 1L, ["clientKey"] = "fixture-client", ["sid"] = "fixture-sid",
        ["udpHost"] = "localhost", ["udpPort"] = 5678, ["encryptionKey"] = "fixture-key",
        ["encryptionIv"] = "fixture-iv", ["encryptionMacKey"] = "fixture-mac"
    })));
});
app.MapControllers();
await app.StartAsync();
try
{
    JsonFiles.Write(Path.Combine(directory, "headers.json"), new Dictionary<string, string> { ["Accept"] = "application/octet-stream" });
    var config = new TargetConfiguration { BaseUrl = app.Urls.Single(), UserId = 1, RequireRotatingToken = true, HeadersFile = "headers.json" };
    using var client = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
    if (args is ["--replay-character-mission", var characterMissionPath, _, var characterMissionOutput])
    {
        await CharacterMissionReplay.Run(client, store, characterMissionPath, characterMissionOutput);
        return;
    }
    if (args is ["--replay-stamp-favorite", var stampFavoritePath, var stampFavoriteOutput])
    {
        await StampFavoriteReplay.Run(client, store, stampFavoritePath, stampFavoriteOutput);
        return;
    }
    if (args is ["--replay-music-video", var videoPath, _, var videoOutput])
    {
        using var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await MusicVideoReplay.Run(client, readback, store, videoPath, videoOutput);
        return;
    }
    if (args is ["--replay-user-config", var configPath, var configOutput])
    {
        using var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await UserConfigReplay.Run(client, readback, store, configPath, configOutput);
        return;
    }
    if (args is ["--replay-login-status", var statusPath, var observerPath, var statusOutput])
    {
        using var observer = new ProtocolClient(new TargetConfiguration { BaseUrl = config.BaseUrl, UserId = 2 },
            directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await LoginStatusReplay.Run(client, observer, store, statusPath, observerPath, statusOutput);
        return;
    }
    if (args is ["--replay-friend", var friendPath, _, var friendOutput])
    {
        using var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        using var peer = new ProtocolClient(new TargetConfiguration { BaseUrl = config.BaseUrl, UserId = 2 },
            directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await FriendReplay.Run(client, readback, peer, store, friendPath, friendOutput);
        return;
    }
    if (args is ["--replay-profile-honor" or "--replay-profile-honor-mission", var honorPath, _, var honorOutput])
    {
        using var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        if (args[0] == "--replay-profile-honor-mission")
            await ProfileHonorReplay.RunMission(client, readback, store, honorPath, honorOutput);
        else
            await ProfileHonorReplay.Run(client, readback, store, honorPath, honorOutput);
        return;
    }
    if (args is ["--replay-honor-mission", var honorMissionPath, _, var honorMissionOutput])
    {
        using var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await HonorMissionReplay.Run(client, readback, store, honorMissionPath, honorMissionOutput);
        return;
    }
    if (args is ["--replay-music-my-list", var myListPath, var myListOutput])
    {
        using var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await MusicMyListReplay.Run(client, readback, store, myListPath, myListOutput);
        return;
    }
    if (args is ["--replay-costume" or "--replay-costume-craft", var costumePath, _, var costumeOutput])
    {
        await CostumeReplay.Run(client, store, costumePath, costumeOutput);
        return;
    }
    if (args is ["--replay-beginner-repeat", var beginnerRepeatPath, _, var beginnerRepeatOutput])
    {
        await LiveMissionReplay.RunBeginnerRepeat(client, store, beginnerRepeatPath, beginnerRepeatOutput);
        return;
    }
    if (args is ["--replay-live-mission" or "--replay-beginner-mission", var missionPath, _, var missionOutput])
    {
        await LiveMissionReplay.Run(client, store, missionPath, missionOutput);
        return;
    }
    if (args is ["--replay-natural-boost", var naturalBefore, var naturalAfter, _, var naturalOutput])
    {
        await BoostReplay.RunNatural(client, store, naturalBefore, naturalAfter, naturalOutput);
        return;
    }
    if (args is ["--replay-boost-item", var boostPath, _, var boostOutput])
    {
        await BoostReplay.Run(client, store, boostPath, boostOutput);
        return;
    }
    if (args is ["--replay-present", var capturePath, var replayOutput])
    {
        await PresentReplay.Run(client, store, capturePath, replayOutput);
        return;
    }
    if (args is ["--replay-login-bonus", var loginPath, _, var loginOutput])
    {
        await LoginBonusReplay.Run(client, store, loginPath, loginOutput);
        return;
    }
    if (args is ["--replay-favorites", var favoriteCaptures, _, var favoriteOutput])
    {
        await FavoriteReplay.Run(client, store, favoriteCaptures, favoriteOutput);
        return;
    }
    if (args is ["--replay-card-practice" or "--replay-special-training" or "--replay-card-image", var practicePath, _, var practiceOutput])
    {
        await CardPracticeReplay.Run(client, store, practicePath, practiceOutput);
        return;
    }
    if (args is ["--replay-skill-practice", var skillPath, var skillReadback, _, var skillOutput])
    {
        await CardPracticeReplay.Run(client, store, skillPath, skillOutput, skillReadback,
            () => new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()));
        return;
    }
    if (args is ["--replay-material-exchange" or "--replay-event-exchange", var exchangePath, _, var exchangeOutput])
    {
        await MaterialExchangeReplay.Run(client, store, exchangePath, exchangeOutput);
        return;
    }
    if (args is ["--replay-vocal-rejection" or "--replay-stamp-rejection", var rejectionPath, _, var rejectionOutput])
    {
        using var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await AreaShopReplay.RunRejected(client, readback, store, rejectionPath, rejectionOutput, args[0] == "--replay-vocal-rejection");
        return;
    }
    if (args is ["--replay-area-shop" or "--replay-music-shop" or "--replay-stamp-shop" or "--replay-vocal-shop", var areaPath, _, var areaOutput])
    {
        await AreaShopReplay.Run(client, store, areaPath, areaOutput, args[0] == "--replay-music-shop", args[0] == "--replay-stamp-shop", args[0] == "--replay-vocal-shop");
        return;
    }
    if (args is ["--replay-live" or "--replay-live-honor" or "--replay-live-result" or "--replay-live-boost" or "--replay-live-achievement" or "--replay-live-experience" or "--replay-live-point", var liveStartPath, var liveClearPath, _, var liveOutput])
    {
        using var liveReadback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await LiveReplay.Run(client, store, liveStartPath, liveClearPath, liveOutput, args[0] == "--replay-live-honor" ? liveReadback : null,
            args[0] == "--replay-live-result" ? liveReadback : null, args[0] == "--replay-live-boost", args[0] == "--replay-live-achievement" ? liveReadback : null,
            args[0] == "--replay-live-experience" ? liveReadback : null, args[0] == "--replay-live-point");
        return;
    }
    if (args is ["--replay-live-start-rejection", var rejectedStart, var startReadback, _, var startOutput])
    {
        using var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await LiveStartReplay.Run(client, readback, store, rejectedStart, startReadback, startOutput);
        return;
    }
    if (args is ["--replay-live-start", var acceptedStart, _, var acceptedStartOutput])
    {
        using var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await LiveStartReplay.RunAccepted(client, readback, store, acceptedStart, acceptedStartOutput);
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
    if (args is ["--replay-challenge-deck" or "--replay-challenge-unlock" or "--replay-challenge-start", var challengeCapture, _, var challengeOutput])
    {
        await ChallengeDeckReplay.Run(client, store, challengeCapture, challengeOutput);
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
    var httpCaptures = Path.Combine(directory, "http");
    Check(Directory.GetFiles(httpCaptures, "*_request.json").Length == 5 &&
        Directory.GetFiles(httpCaptures, "*_response.json").Length == 5,
        "每个实际 HTTP 请求均留存成对文件，包括写入前后的自动回读");
    var systemCapture = JsonNode.Parse(File.ReadAllText(Path.Combine(httpCaptures, "0001_GET_api_system_response.json")))!;
    Check(systemCapture["loginBonusStatusHeader"]?[0]?.GetValue<string>() == "true",
        "逐包记录保留登录奖励状态提示，不展开敏感响应头");
    Check(systemCapture["httpStatus"]!.GetValue<int>() == 200 && systemCapture["body"]?["appVersions"] is JsonArray,
        "HTTP 抓包使用方法和路由命名，保存解密后的响应");
    var deckCapture = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(httpCaptures, "0004_*_request.json").Single()))!;
    Check(deckCapture["body"]?["userDeckUpdates"]?[0]?["userDeck"]?["userId"]?.GetValue<string>() == "<redacted>",
        "HTTP 请求记录包含实际注入的账号字段并脱敏");
    Check(Directory.GetFiles(httpCaptures).All(file => !File.ReadAllText(file).Contains(token)),
        "HTTP 抓包不保存轮换 token");
    Fails(() => client.CaptureTo(httpCaptures), "已有抓包目录不能覆盖");
    using (var disconnected = new ProtocolClient(new() { BaseUrl = "http://127.0.0.1:0" }, directory,
        ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
    {
        var disconnectedDirectory = Path.Combine(directory, "disconnected");
        disconnected.CaptureTo(disconnectedDirectory);
        var disconnectedFailed = false;
        try { await disconnected.Send(new() { Operation = "system" }); }
        catch (HttpRequestException) { disconnectedFailed = true; }
        var disconnectedResponse = JsonNode.Parse(File.ReadAllText(Path.Combine(disconnectedDirectory,
            "0001_GET_api_system_response.json")))!;
        Check(disconnectedFailed && disconnectedResponse["status"]!.GetValue<string>() == "failed" &&
            disconnectedResponse["httpStatus"] == null, "网络失败保留成对记录，不虚构 HTTP 状态");
    }
    Check(validHeaders, "配置 Accept 不重复，GET 与写请求都携带二进制 Content-Type");
    var record = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "003.json")))!.AsObject();
    Check(record["status"]!.GetValue<string>() == "completed" && record["stateChanges"]!.AsArray().Count > 0, "记录状态变化");
    Check(!record.ToJsonString().Contains(token), "不留存轮换 token");
    Check(record["after"]!["userRegistration"]!["userId"]!.GetValue<string>() == "<redacted>", "账号字段脱敏");
    var guardedStep = new ScenarioStep
    {
        Operation = "deck-save", Body = scenario.Steps[2].Body,
        RequireReleaseConditionIds = [90001]
    };
    var guardedScenario = new Scenario { Steps = [guardedStep] };
    ScenarioRunner.Validate(guardedScenario, [config], new HashSet<string>());
    var guardedBefore = requests;
    var guardedOutput = Path.Combine(directory, "guarded-missing");
    var guardedStopped = false;
    try { await ScenarioRunner.Run(client, guardedScenario, guardedOutput); }
    catch (ClientFailure) { guardedStopped = true; }
    var guardedRecord = JsonNode.Parse(File.ReadAllText(Path.Combine(guardedOutput, "001.json")))!;
    Check(guardedStopped && requests == guardedBefore + 1 &&
        guardedRecord["failurePhase"]!.GetValue<string>() == "release-condition" &&
        guardedRecord["missingReleaseConditionIds"]![0]!.GetValue<int>() == 90001,
        "未解锁时只回读一次，记录缺少的条件，不发送写请求");
    var guardedState = store.Read(1)!;
    var priorConditions = guardedState.Data.userReleaseConditions;
    guardedState.Data.userReleaseConditions = [new UserReleaseCondition { releaseConditionId = 90001 }];
    store.Save(1, guardedState);
    guardedBefore = requests;
    await ScenarioRunner.Run(client, guardedScenario, Path.Combine(directory, "guarded-released"));
    Check(requests == guardedBefore + 3, "已解锁时沿用原会话完成写入及前后回读");
    guardedState = store.Read(1)!;
    guardedState.Data.userReleaseConditions = priorConditions;
    store.Save(1, guardedState);
    Fails(() => ScenarioRunner.Validate(new() { Steps = [new()
    {
        Operation = "system", RequireReleaseConditionIds = [90001]
    }] }, [config], new HashSet<string>()), "不支持回读的操作不能配置解锁检查");
    Fails(() => ScenarioRunner.Validate(new() { Steps = [new()
    {
        Operation = "deck-save", Body = guardedStep.Body, RequireReleaseConditionIds = [0]
    }] }, [config], new HashSet<string>()), "解锁条件必须为正整数");
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
    await ScenarioRunner.Run(client, new() { Steps = [new()
    {
        Operation = "story-read", Args = new() { ["storyType"] = "special_story", ["episodeId"] = "991" }
    }] }, Path.Combine(directory, "special-story-no-content"));
    var specialNoContent = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "special-story-no-content/001.json")))!;
    Check(specialNoContent["httpStatus"]!.GetValue<int>() == 204 &&
        specialNoContent["status"]!.GetValue<string>() == "completed",
        "特殊剧情重复阅读的 204 空响应完成会话轮换及状态回读");
    noContentPath = "/api/user/1/story/event_story/episode/991";
    using (var unsupportedClient = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
    {
        await unsupportedClient.Send(new() { Operation = "system" });
        var rejected = false;
        try { await unsupportedClient.Send(new() { Operation = "story-read", Args = new() { ["storyType"] = "event_story", ["episodeId"] = "991" } }); }
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
    await CardHttpChecks.Run(client, config, store, directory, Check, app.Services);
    await ShopHttpChecks.Run(client, store, directory, Check);
    Check(shopRequests.SequenceEqual(new (string, long?)[] { ("POST", 0), ("PUT", 0), ("POST", 0) }),
        "商店购买与升级分别使用 POST 和 PUT，均不发送 body");
    await GachaHttpChecks.Run(client, store, directory, Check);
    await LiveHttpChecks.Run(client, config, store, directory, Check);
    await HomeHttpChecks.Run(client, store, directory, Check);
    await FriendMissionHttpChecks.Run(client, store, Check);
    await BeginnerCompletionHttpChecks.Run(client, store, Check);
    await MissionHttpChecks.Run(client, config, store, directory, Check);
    await StoryHttpChecks.Run(client, store, directory, Check);
    await BookmarkHttpChecks.Run(client, store, directory, Check);
    await FavoriteHttpChecks.Run(client, store, directory, Check);
    await ProfileHttpChecks.Run(client, store, directory, Check);
    await StampFavoriteHttpChecks.Run(client, store, directory, Check);
    await MusicMyListHttpChecks.Run(client, config, store, directory, Check);
    await CostumeHttpChecks.Run(client, store, app.Services, directory, Check);
    await CostumeHttpChecks.RunCraft(client, store, app.Services, directory, Check);
    await CustomProfileHttpChecks.Run(client, store, directory, Check);
    Check(imageRequests == 2 && !privateImageHeaders, "图片请求不发送 API 凭证且不消耗轮换 token");
    Fails(() => ThumbnailDownload.ValidatePath("https://example.invalid/image/custom-profile-card/thumbnail/a/b"), "图片拒绝外部 URL");
    Fails(() => ThumbnailDownload.ValidatePath("image/custom-profile-card/thumbnail/../b"), "图片拒绝路径穿越");
    await AccountReadHttpChecks.Run(client, store, directory, Check);
    await SuiteMasterHttpChecks.Run(client, config, directory, Check);
    var realtimeResponse = await client.Send(new() { Operation = "diarkis-auth", Args = new() { ["diarkisServerType"] = "multi" } });
    Check(realtimeResponse["clientKey"]!.GetValue<string>() == "fixture-client" &&
        client.LastResponse!["clientKey"]!.GetValue<string>() == "<redacted>" &&
        client.LastResponse!["sid"]!.GetValue<string>() == "<redacted>", "实时认证返回可用原对象，记录副本隐藏连接凭证");
    Check(!realtimeResponse.ContainsKey("tcpHost") && !realtimeResponse.ContainsKey("tcpPort") &&
        realtimeResponse["udpPort"]!.GetValue<int>() == 5678, "实时认证接受官方仅返回UDP连接信息的响应");
    await client.Send(new() { Operation = "suite" });
    Check(client.LastHttpStatus == 200, "实时认证后沿用已轮换的HTTP会话");
    await InheritHttpChecks.Run(client, config, store, directory, Check);
    await ChallengeDeckHttpChecks.Run(client, config, store, directory, Check);
    await MaterialExchangeHttpChecks.Run(client, config, store, directory, Check);
    await EventExchangeHttpChecks.Run(client, config, store, directory, Check);
    await CharacterMissionHttpChecks.Run(client, store, app.Services, directory, Check);
    Fails(() => Operations.Path(Operations.All["custom-profile-card-delete"], new() { Args = new() { ["customProfileId"] = "1" } }, 1),
        "名片删除必须提供卡片 ID 列表");
    Fails(() => ScenarioRunner.Validate(new() { Steps = [new() { Operation = "live-clear", UseLiveSession = true, Body = new() }] },
        [config], new HashSet<string>()), "未开局不能引用 Live ID");
    Fails(() => ScenarioRunner.Validate(new() { Steps = [new() { Operation = "suite", DelayBeforeMs = -1 }] },
        [config], new HashSet<string>()), "拒绝负数等待，避免无限等待");
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
    Check(Operations.Path(Operations.All["material-exchange"], new()
    {
        Args = new() { ["materialExchangeId"] = "2" }, Query = new() { ["costGroupId"] = "0", ["count"] = "2" }
    }, 1) == "/api/user/1/material-exchange/2?costGroupId=0&count=2", "材料兑换以整数 query 发送成本组和次数");
    foreach (var value in new[] { "0", "-1", "1&costGroupId=2", "2147483648" })
        Fails(() => Operations.Path(Operations.All["material-exchange"], new()
        {
            Args = new() { ["materialExchangeId"] = "2" }, Query = new() { ["costGroupId"] = "1", ["count"] = value }
        }, 1), "材料兑换拒绝无效次数和 query 注入");
    Fails(() => Operations.Path(Operations.All["material-exchange"], new()
    {
        Args = new() { ["materialExchangeId"] = "2" }, Query = new() { ["count"] = "1" }
    }, 1), "材料兑换必须明确成本组");
    foreach (var (offset, phase) in new[] { (1, "before-snapshot"), (2, "request"), (3, "after-snapshot") })
    {
        using var interrupted = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await interrupted.Send(new() { Operation = "system" });
        var beforeRequests = requests;
        failAtRequest = beforeRequests + offset;
        var output = Path.Combine(directory, "failure-" + phase);
        var interruptedStep = new ScenarioStep { Operation = "deck-save", Body = JsonNode.Parse(
            """{"userDeckUpdates":[{"userDeck":{"deckId":201,"name":"诊断","leader":1,"member1":1}}]}""")!.AsObject() };
        var stopped = false;
        try { await ScenarioRunner.Run(interrupted, new() { Steps = [interruptedStep] }, output); }
        catch (ClientFailure) { stopped = true; }
        failAtRequest = null;
        var failure = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!;
        Check(stopped && failure["failurePhase"]!.GetValue<string>() == phase && requests == beforeRequests + offset,
            "失败记录标明实际请求阶段且不自动重发");
        var failedHttp = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(Path.Combine(output, "http"),
            $"{offset:D4}_*_response.json").Single()))!;
        Check(failedHttp["httpStatus"]!.GetValue<int>() == 503 && failedHttp["status"]!.GetValue<string>() == "received",
            "HTTP 错误响应独立留存，不被自动回读覆盖");
        Check((store.Read(1)!.Data.userDecks.Any(d => d.deckId == 201)) == (phase == "after-snapshot"),
            "写前或写入失败不改变编队，写后读取失败保留已提交结果");
        if (phase == "after-snapshot")
            Check(failure["httpStatus"]!.GetValue<int>() == 200 && failure["response"] != null &&
                failure["lastHttpStatus"]!.GetValue<int>() == 503,
                "回读失败时仍保留原写请求成功响应及两个独立状态码");
    }
    await client.Send(new() { Operation = "system" });
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
    foreach (var serverType in new[] { "udp", "multi", "cheerful", "virtual_live", "streaming_live", "rank_match", "mysekai", "custom_multi" })
        Check(Operations.Path(Operations.All["diarkis-auth"], new() { Args = new() { ["diarkisServerType"] = serverType } }, 1)
            == "/api/user/1/diarkis-auth?diarkisServerType=" + serverType, "实时认证支持客户端枚举中的服务器类型");
    Fails(() => Operations.Path(Operations.All["diarkis-auth"], new() { Args = new() { ["diarkisServerType"] = "multi&x=1" } }, 1), "实时认证拒绝额外查询注入");
    Fails(() => Operations.Path(Operations.All["diarkis-auth"], new(), 1), "实时认证必须指定服务器类型");
    redactor.AddSecret("fixture-secret");
    var cleaned = redactor.Clean(JsonNode.Parse("""{"sessionToken":"fixture-secret","nested":{"detail":"contains fixture-secret","deviceId":"private"}}"""))!.ToJsonString();
    Check(!cleaned.Contains("fixture-secret") && !cleaned.Contains("private"), "凭证字段与已知秘密嵌套脱敏");
    var realtimeAuth = JsonNode.Parse("""{"clientKey":"fixture-client-key","sid":"fixture-session","encryptionKey":"fixture-key","encryptionIv":"fixture-iv","encryptionMacKey":"fixture-mac","tcpPort":1234,"udpPort":5678,"nested":[{"SID":"fixture-nested-session"}],"side":"left","musicKey":7}""")!;
    var realtimeCleaned = redactor.Clean(realtimeAuth)!;
    Check(new[] { "clientKey", "sid", "encryptionKey", "encryptionIv", "encryptionMacKey" }
        .All(key => realtimeCleaned[key]!.GetValue<string>() == "<redacted>") &&
        realtimeCleaned["nested"]![0]!["SID"]!.GetValue<string>() == "<redacted>", "实时认证凭证无需预先登记即可嵌套脱敏");
    Check(realtimeCleaned["tcpPort"]!.GetValue<int>() == 1234 && realtimeCleaned["udpPort"]!.GetValue<int>() == 5678 &&
        realtimeCleaned["side"]!.GetValue<string>() == "left" && realtimeCleaned["musicKey"]!.GetValue<int>() == 7,
        "实时凭证匹配不误删端口或名称相近的业务字段");
    Check(realtimeAuth["clientKey"]!.GetValue<string>() == "fixture-client-key" &&
        realtimeAuth["nested"]![0]!["SID"]!.GetValue<string>() == "fixture-nested-session", "脱敏不修改运行中的实时认证对象");
    Fails(() => new TargetConfiguration { BaseUrl = "https://example.invalid" }.Validate(), "不能将远端伪装为 local");
    Fails(() => ScenarioRunner.Validate(scenario, [new() { Kind = "official", BaseUrl = "https://example.invalid" }], new HashSet<string>()), "官方写入需显式列出操作");
    Fails(() => Operations.Path(Operations.All["favorite-delete"], new() { Args = new() { ["shareNo"] = "../auth" } }, 1), "拒绝路径注入");
    Check(Operations.Path(Operations.All["character-mission-receive-all"],
        new() { Args = new() { ["characterId"] = "1" } }, 1) == "/api/user/1/character/1/mission",
        "角色任务全部领取无额外路径和请求体");
    foreach (var type in new[] { "COLLECT_COSTUME_3D", "achievement" })
        Check(Operations.Path(Operations.All["character-mission-receive"],
            new() { Args = new() { ["characterId"] = "1", ["characterMissionType"] = type } }, 1) ==
            "/api/user/1/character/1/mission/" + type, "角色任务类型遵循大写枚举与 achievement 特例");
    foreach (var type in new[] { "OTHER", "collect_costume_3d", "../auth", "UNKNOWN" })
        Fails(() => Operations.Path(Operations.All["character-mission-receive"],
            new() { Args = new() { ["characterId"] = "1", ["characterMissionType"] = type } }, 1),
            "角色任务拒绝错误大小写、未映射枚举和路径注入");
    Fails(() => Operations.EncodeBody(Operations.All["character-mission-receive"], new()), "角色任务领取不接受请求体");
    Console.WriteLine($"Client：通过 {count} 项检查；隔离 HTTP 服务即将关闭。");
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
        Type[] tested = [typeof(DeckController), typeof(ChallengeLiveController), typeof(PresentController), typeof(CardController), typeof(ShopController), typeof(GachaController), typeof(LiveController), typeof(MusicVideoController), typeof(HomeController), typeof(LoginStatusController), typeof(MiscController), typeof(MissionController), typeof(ProfileController), typeof(ProfileHonorController), typeof(UserConfigController), typeof(MusicMyListController), typeof(CustomProfileController), typeof(LoginController), typeof(InheritController), typeof(StoryController), typeof(StoryBookmarkController), typeof(StoryFavoriteController)];
        foreach (var controller in feature.Controllers.Where(c => c.AsType() != typeof(FriendController) &&
                     c.AsType() != typeof(PrivateSekai.Modules.Platform.SuiteMasterFileController) && !tested.Contains(c.AsType())).ToArray())
            feature.Controllers.Remove(controller);
    }
}
