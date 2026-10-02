using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Transport;

public sealed class MasterRequestScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, MasterData masterData)
    {
        using (masterData.BeginRequest())
            await next(context);
    }
}
