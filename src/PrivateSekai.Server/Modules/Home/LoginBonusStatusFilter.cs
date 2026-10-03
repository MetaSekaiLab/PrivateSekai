using System.Globalization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Home;

public sealed class LoginBonusStatusFilter(UserOperation operations) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context) { }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception != null || context.HttpContext.Response.StatusCode >= 400 ||
            context.Result is IStatusCodeActionResult { StatusCode: >= 400 } ||
            !long.TryParse(context.RouteData.Values["userId"]?.ToString(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var userId)) return;
        var state = operations.Read(userId);
        if (state == null) return;
        // 当前只接入首次发放；跨日资格随日次推进一起补充。
        context.HttpContext.Response.Headers["X-Login-Bonus-Status"] =
            state.Data.userLoginBonuses?.Length > 0 ? "false" : "true";
    }
}
