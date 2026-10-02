using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PrivateSekai.Protocol;

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
            var bytes = DumpSerializer.SerializeObject(obj.Value);
            context.Result = new FileContentResult(bytes, "application/octet-stream");
        }

        await next();
    }
}
