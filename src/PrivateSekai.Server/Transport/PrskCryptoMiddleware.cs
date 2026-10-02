using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using PrivateSekai.Config;

namespace PrivateSekai.Transport;

public sealed class PrskCryptoMiddleware(RequestDelegate next)
{
    public const string DecryptedItemKey = "Prsk.Decrypted";

    private static readonly byte[] EmptyMap = [0x80];

    public async Task InvokeAsync(HttpContext ctx)
    {
        var endpoint = ctx.GetEndpoint();
        var decrypt = endpoint?.Metadata.GetMetadata<PrskDecryptRequestAttribute>() is not null;
        var encrypt = endpoint?.Metadata.GetMetadata<PrskEncryptResponseAttribute>() is not null
            && endpoint.Metadata.GetMetadata<PrskPlaintextResponseAttribute>() is null;

        if (decrypt)
            await DecryptRequestAsync(ctx, endpoint!);

        if (!encrypt)
        {
            await next(ctx);
            return;
        }

        var originalBody = ctx.Response.Body;
        await using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        try
        {
            await next(ctx);
        }
        finally
        {
            ctx.Response.Body = originalBody;
        }

        var plaintext = buffer.ToArray();
        ctx.Response.Headers.ContentLength = null;

        if (ctx.Response.StatusCode == StatusCodes.Status204NoContent)
        {
            ctx.Response.ContentType = null;
            return;
        }

        var encrypted = PrskCrypto.EncryptAesCbc(plaintext);
        ctx.Response.ContentType = "application/octet-stream";
        ctx.Response.ContentLength = encrypted.Length;
        await ctx.Response.Body.WriteAsync(encrypted);
    }

    private static async Task DecryptRequestAsync(HttpContext ctx, Endpoint endpoint)
    {
        if (ctx.Request.ContentLength == 0)
            return;

        if (!HttpMethods.IsPost(ctx.Request.Method)
            && !HttpMethods.IsPut(ctx.Request.Method)
            && !HttpMethods.IsPatch(ctx.Request.Method))
            return;

        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms);
        var ciphertext = ms.ToArray();
        if (ciphertext.Length == 0)
            return;

        byte[] msgpack;
        if (ciphertext.AsSpan().SequenceEqual(ServerConfig.EmptyRequestCiphertext))
            msgpack = EmptyMap;
        else if (endpoint.Metadata.GetMetadata<PrskOptionalBodyAttribute>() is not null)
            msgpack = PrskCrypto.TryDecryptAesCbc(ciphertext, out var decrypted) ? decrypted : EmptyMap;
        else
            msgpack = PrskCrypto.DecryptAesCbc(ciphertext);

        ctx.Items[DecryptedItemKey] = true;
        ctx.Request.Body = new MemoryStream(msgpack);
        ctx.Request.ContentLength = msgpack.Length;
    }
}
