using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrivateSekai.Config;
using PrivateSekai.Crypto;
using PrivateSekai.Middleware;
using PrivateSekai.Services;
using PrivateSekai.Services.Master;

var builder = WebApplication.CreateBuilder(args);

ServerConfig.Load(builder.Configuration, builder.Environment.ContentRootPath);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(ServerConfig.Port);
});

builder.Services.AddSingleton<UserManager>();
builder.Services.AddSingleton(_ => new MasterDataManager(ServerConfig.MasterCache));
builder.Services.AddControllers(options =>
    options.InputFormatters.Insert(0, new PrskMessagePackInputFormatter()));

var app = builder.Build();
var appLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PrivateSekai");

app.Use(async (ctx, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    await next();
    sw.Stop();
    appLogger.LogInformation("{Method} {Path}{QueryString} → {StatusCode} ({Elapsed}ms)",
        ctx.Request.Method, ctx.Request.Path, ctx.Request.QueryString, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
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

MasterDataManager.Bind(app.Services.GetRequiredService<MasterDataManager>());
app.Services.GetRequiredService<UserManager>();

if (!Directory.Exists(ServerConfig.TemplatePath))
    Console.Error.WriteLine($"WARNING: template directory not found: {ServerConfig.TemplatePath}");
if (!Directory.Exists(ServerConfig.SuiteMasterFilePath))
    Console.Error.WriteLine($"WARNING: suitemasterfile directory not found: {ServerConfig.SuiteMasterFilePath}");
if (!Directory.Exists(ServerConfig.SekaiMasterDbDiffPath))
    Console.Error.WriteLine($"WARNING: sekai-master-db-diff directory not found: {ServerConfig.SekaiMasterDbDiffPath}");

Console.WriteLine($"Private Sekai is running on {ServerConfig.Port}");
app.Run();

