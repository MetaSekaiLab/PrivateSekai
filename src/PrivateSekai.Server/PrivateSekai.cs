using System;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrivateSekai;
using PrivateSekai.Config;
using PrivateSekai.Models;
using PrivateSekai.Modules.Accounts;
using PrivateSekai.Modules.Home;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

var builder = WebApplication.CreateBuilder(args);

ServerConfig.Load(builder.Configuration, builder.Environment.ContentRootPath);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Parse(builder.Configuration["PrivateSekai:ListenAddress"] ?? "0.0.0.0"), ServerConfig.Port);
});

builder.Services.AddPrivateSekai();

var app = builder.Build();
var appLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PrivateSekai");

app.Use(async (ctx, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    await next();
    sw.Stop();
    appLogger.LogInformation("{Method} {Path} → {StatusCode} ({Elapsed}ms)",
        ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
});

app.UseExceptionHandler(handler => handler.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    if (ex != null)
        appLogger.LogError(ex.Error, "Unhandled: {Message}", ex.Error.Message);

    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new
    {
        error = "Internal Server Error",
        detail = ex?.Error.Message ?? "Unknown error"
    });
}));

app.UseRouting();
app.UseMiddleware<MasterRequestScopeMiddleware>();
app.UseMiddleware<PrskCryptoMiddleware>();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var clock = services.GetRequiredService<TimeProvider>();
    var store = services.GetRequiredService<IUserStore>();
    if (store.Read(0) == null)
        store.Save(0, services.GetRequiredService<AccountTemplates>().CreateUser(0, clock.GetUtcNow().ToUnixTimeMilliseconds()));
    services.GetRequiredService<ResourceService>();
    services.GetRequiredService<UserOperation>().Execute(0, () =>
    {
        services.GetRequiredService<HomeService>().EnsureShopAreaActionSets();
        services.GetRequiredService<UserSession>().NormalizeEventBreakTime();
        return new EmptyResponse();
    });
}

if (!Directory.Exists(ServerConfig.TemplatePath))
    Console.Error.WriteLine($"WARNING: template directory not found: {ServerConfig.TemplatePath}");
if (!Directory.Exists(ServerConfig.SuiteMasterFilePath))
    Console.Error.WriteLine($"WARNING: suitemasterfile directory not found: {ServerConfig.SuiteMasterFilePath}");
if (!Directory.Exists(ServerConfig.SekaiMasterDbDiffPath))
    Console.Error.WriteLine($"WARNING: sekai-master-db-diff directory not found: {ServerConfig.SekaiMasterDbDiffPath}");

Console.WriteLine($"Private Sekai is running on {ServerConfig.Port}");
app.Run();

