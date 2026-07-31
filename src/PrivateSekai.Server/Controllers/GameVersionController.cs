using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Config;

namespace PrivateSekai.Controllers;

public class GameVersionController : PrskController
{
    /// <summary>
    /// 获取客户端启动用的 AppInfo。客户端用当前版本标识和 app hash 拼出完整 URL，请求成功后写入运行时环境配置，后续登录、服务器时间同步、资源域名和资源版本相关流程会依赖这份信息。
    /// </summary>
    [HttpGet("/{gameVersion}/{appHash}")]
    public IActionResult GetGameVersion(string gameVersion, string appHash)
    {
        return Ok(new
        {
            profile = "production",
            assetbundleHostHash = "cf2d2388",
            domain = ServerConfig.GameVersionDomain,
        });
    }
}
