extern alias game;

using Microsoft.AspNetCore.Mvc;
using game::Sekai;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Platform;

public sealed class MaintenanceController : PrskController
{
    [HttpGet("api/module-maintenance/{kind}")]
    public IActionResult HandleModuleMaintenance(string kind) => Ok(new ModuleMaintenanceResponse
    {
        moduleMaintenanceType = kind.ToLowerInvariant(),
        isOngoing = false
    });
}
