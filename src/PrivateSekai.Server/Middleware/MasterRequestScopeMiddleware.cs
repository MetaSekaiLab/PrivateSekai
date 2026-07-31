using PrivateSekai.Services.Master;

namespace PrivateSekai.Middleware;

public sealed class MasterRequestScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, MasterDataManager masterData)
    {
        using (masterData.BeginRequest())
            await next(context);
    }
}
