using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PrivateSekai.Crypto;

public sealed class PrskDecryptRequestAttribute : Attribute;

public sealed class PrskOptionalBodyAttribute : Attribute;

public sealed class PrskPlaintextResponseAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PrskEncryptResponseAttribute : Attribute, IAsyncResultFilter, IOrderedFilter
{
    public int Order => -1000;

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is PrskPlaintextResponseAttribute))
        {
            await next();
            return;
        }

        if (context.Result is ObjectResult { Value: not null, StatusCode: null or >= 200 and < 300 } obj)
        {
            var bytes = PrskCrypto.SerializeSkipNull(obj.Value);
            context.Result = new FileContentResult(bytes, "application/octet-stream");
        }

        await next();
    }
}
