using PrivateSekai.Config;

namespace PrivateSekai.Crypto;

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

        await next(ctx);

        buffer.Position = 0;
        var plaintext = buffer.ToArray();
        ctx.Response.Body = originalBody;
        ctx.Response.Headers.ContentLength = null;

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
