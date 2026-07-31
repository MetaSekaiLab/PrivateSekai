using MessagePack;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace PrivateSekai.Crypto;

public sealed class PrskMessagePackInputFormatter : InputFormatter
{
    public PrskMessagePackInputFormatter()
    {
        SupportedMediaTypes.Add("application/octet-stream");
    }

    public override bool CanRead(InputFormatterContext context) =>
        context.HttpContext.Items.ContainsKey(PrskCryptoMiddleware.DecryptedItemKey);

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        var model = await MessagePackSerializer.DeserializeAsync(
            context.ModelType,
            context.HttpContext.Request.Body,
            cancellationToken: context.HttpContext.RequestAborted);
        return await InputFormatterResult.SuccessAsync(model);
    }
}
