extern alias game;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using game::Sekai;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;
using PrivateSekai.Transport;

namespace PrivateSekai.Tests;

internal static class TransportChecks
{
    public static void Run()
    {
        Configure();
        using var mvc = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();
        VerifyControllerRoundTrip(mvc).GetAwaiter().GetResult();
        VerifyObjectResult(mvc).GetAwaiter().GetResult();
        VerifyFailure(mvc);
        VerifyPlaintext(mvc).GetAwaiter().GetResult();
    }

    private static async Task VerifyControllerRoundTrip(IServiceProvider mvc)
    {
        var state = TestUsers.Create(1);
        state.Data.userProfile = new UserProfile { word = "before" };
        state.Data.userCards = [new() { cardId = 7 }];
        var store = new MemoryUserStore();
        store.Save(1, state);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        using var thumbnails = new CustomProfileThumbnailStore();
        var controller = new ProfileController(operation, session,
            new ProfileService(session, thumbnails, ProfileChecks.Master()), null!);
        var context = CreateContext(mvc, new PrskDecryptRequestAttribute(), new PrskEncryptResponseAttribute());
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        using var originalBody = new MemoryStream();
        context.Response.Body = originalBody;
        var request = new PutUserProfileRequest { word = "profile-fixture", profileImageType = "card_before_special_training", profileImageId = 7 };
        var ciphertext = PrskCrypto.PrskEnc(request);
        context.Request.Method = HttpMethods.Put;
        context.Request.ContentType = "application/octet-stream";
        context.Request.ContentLength = ciphertext.Length;
        using var requestBody = new MemoryStream(ciphertext);
        context.Request.Body = requestBody;
        byte[]? encoded = null;

        var middleware = new PrskCryptoMiddleware(async ctx =>
        {
            Check.That(ctx.Items.ContainsKey(PrskCryptoMiddleware.DecryptedItemKey), "中间件标记已解密请求");
            var formatter = new PrskMessagePackInputFormatter();
            var formatterContext = new InputFormatterContext(ctx, "body", new ModelStateDictionary(),
                new EmptyModelMetadataProvider().GetMetadataForType(typeof(PutUserProfileRequest)),
                (stream, encoding) => new StreamReader(stream, encoding));
            Check.That(formatter.CanRead(formatterContext), "MessagePack formatter识别已解密请求");
            var parsed = (PutUserProfileRequest)(await formatter.ReadRequestBodyAsync(formatterContext)).Model!;
            Check.That(parsed.word == request.word && parsed.profileImageId == 7, "加密请求经中间件和formatter还原DTO");
            var result = (FileContentResult)controller.HandleUserProfile(1, parsed);
            encoded = result.FileContents.ToArray();
            await ExecuteResultAsync(ctx, result, controller, filtered =>
                Check.That(ReferenceEquals(result, filtered), "编码后的Controller文件响应不被filter二次序列化"));
        });

        await middleware.InvokeAsync(context);
        var encrypted = originalBody.ToArray();
        var decrypted = PrskCrypto.DecryptAesCbc(encrypted);
        var response = DumpSerializer.Deserialize<SuiteUserCommonResponse>(decrypted);
        Check.That(encoded != null && decrypted.SequenceEqual(encoded), "中间件仅加密一次原始MessagePack响应字节");
        Check.That(response.updatedResources!.userProfile!.word == request.word &&
                   response.updatedResources.userProfile.profileImageId == 7, "真实Controller加密响应可解回预期DTO");
        Check.That(store.Read(1)!.Data.userProfile!.word == request.word, "成功响应对应用户操作已提交状态");
        Check.That(ReferenceEquals(context.Response.Body, originalBody) &&
                   context.Response.ContentLength == encrypted.Length && context.Response.ContentType == "application/octet-stream",
            "加密响应恢复原流并设置密文长度与类型");
        context.Request.Body.Dispose();
    }

    private static async Task VerifyObjectResult(IServiceProvider mvc)
    {
        var context = CreateContext(mvc, new PrskEncryptResponseAttribute());
        using var output = new MemoryStream();
        context.Response.Body = output;
        var dto = new UserCard { cardId = 17, level = 4 };
        var middleware = new PrskCryptoMiddleware(ctx => ExecuteResultAsync(ctx, new ObjectResult(dto), new object(),
            filtered => Check.That(filtered is FileContentResult, "对象响应由filter转换为MessagePack文件响应")));

        await middleware.InvokeAsync(context);
        var plaintext = PrskCrypto.DecryptAesCbc(output.ToArray());
        Check.That(plaintext.SequenceEqual(DumpSerializer.Serialize(dto)) &&
                   DumpSerializer.Deserialize<UserCard>(plaintext).level == 4,
            "对象响应经过filter和中间件只序列化一次并保持字段");
    }

    private static void VerifyFailure(IServiceProvider mvc)
    {
        var store = new MemoryUserStore();
        store.Save(1, TestUsers.Create(1));
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var context = CreateContext(mvc, new PrskEncryptResponseAttribute());
        using var originalBody = new MemoryStream();
        originalBody.WriteByte(0x11);
        context.Response.Body = originalBody;
        var middleware = new PrskCryptoMiddleware(ctx =>
        {
            ctx.Response.Body.WriteByte(0x7f);
            operation.Execute(1, () =>
            {
                session.Data.userGamedata!.coin = 0;
                throw new InvalidOperationException("fixture failure");
            });
            return Task.CompletedTask;
        });

        Check.Throws<InvalidOperationException>(() => middleware.InvokeAsync(context).GetAwaiter().GetResult(),
            "业务异常向上传播而不产生成功密文");
        Check.That(ReferenceEquals(context.Response.Body, originalBody) && originalBody.ToArray().SequenceEqual(new byte[] { 0x11 }),
            "异常路径恢复原响应流并丢弃部分响应");
        Check.That(store.Read(1)!.Data.userGamedata!.coin == 100, "响应中间件内业务失败不提交用户状态");
    }

    private static async Task VerifyPlaintext(IServiceProvider mvc)
    {
        var unmarked = CreateContext(mvc);
        using var unmarkedOutput = new MemoryStream();
        unmarked.Response.Body = unmarkedOutput;
        byte[] bytes = [0x01, 0x02, 0x03, 0x04];
        await new PrskCryptoMiddleware(ctx => ctx.Response.Body.WriteAsync(bytes).AsTask()).InvokeAsync(unmarked);
        Check.That(unmarkedOutput.ToArray().SequenceEqual(bytes) && ReferenceEquals(unmarked.Response.Body, unmarkedOutput),
            "无加密标记的endpoint原样输出字节");

        var plaintext = CreateContext(mvc, new PrskEncryptResponseAttribute(), new PrskPlaintextResponseAttribute());
        using var plaintextOutput = new MemoryStream();
        plaintext.Response.Body = plaintextOutput;
        var result = new ObjectResult(new { message = "plaintext-fixture" });
        await new PrskCryptoMiddleware(ctx => ExecuteResultAsync(ctx, result, new object(), filtered =>
            Check.That(ReferenceEquals(filtered, result), "明文标记阻止filter转换对象响应"))).InvokeAsync(plaintext);
        using var json = JsonDocument.Parse(plaintextOutput.ToArray());
        Check.That(json.RootElement.GetProperty("message").GetString() == "plaintext-fixture" &&
                   plaintext.Response.ContentType?.StartsWith("application/json", StringComparison.Ordinal) == true,
            "明文标记覆盖加密标记并保留正常JSON响应");
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services, params object[] metadata)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "transport-fixture"));
        return context;
    }

    private static async Task ExecuteResultAsync(HttpContext context, IActionResult result, object controller, Action<IActionResult> inspect)
    {
        var descriptor = new ActionDescriptor { EndpointMetadata = context.GetEndpoint()!.Metadata.ToList() };
        var action = new ActionContext(context, new RouteData(), descriptor);
        var filter = new PrskEncryptResponseAttribute();
        List<IFilterMetadata> filters = [filter];
        var executing = new ResultExecutingContext(action, filters, result, controller);
        await filter.OnResultExecutionAsync(executing, async () =>
        {
            inspect(executing.Result);
            await executing.Result.ExecuteResultAsync(action);
            return new ResultExecutedContext(action, filters, executing.Result, controller);
        });
    }

    private static void Configure()
    {
        var values = new Dictionary<string, string?>
        {
            ["PrivateSekai:AesKey"] = new('k', 16),
            ["PrivateSekai:AesIv"] = new('i', 16),
            ["PrivateSekai:JwtKey"] = new('j', 32),
            ["PrivateSekai:EmptyRequestCiphertext"] = "00",
            ["PrivateSekai:IgnoreInvalidCredential"] = "false",
            ["PrivateSekai:SkipTutorial"] = "false",
            ["PrivateSekai:Debug"] = "false",
            ["PrivateSekai:Port"] = "0",
            ["PrivateSekai:GameVersionDomain"] = "example.invalid",
            ["PrivateSekai:Paths:Template"] = AppContext.BaseDirectory,
            ["PrivateSekai:Paths:SuiteMasterFile"] = AppContext.BaseDirectory,
            ["PrivateSekai:Paths:SekaiMasterDbDiff"] = AppContext.BaseDirectory
        };
        ServerConfig.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), AppContext.BaseDirectory);
    }
}
