using System;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Diagnostics;

[ApiController]
public sealed class DebugController(UserOperation operations, TimeProvider clock) : ControllerBase
{
    [HttpGet("favicon.ico")]
    public IActionResult Favicon() => NoContent();

    [HttpGet("metamiku/debug/getUserList")]
    public IActionResult GetUserList() => !ServerConfig.Debug
        ? StatusCode(403, "Debug mode is off")
        : Content(JsonSerializer.Serialize(operations.GetUserIds().Select(id => new { userId = id })), "application/json");

    [HttpGet("metamiku/debug/getUserSuiteData/{userId}")]
    public IActionResult GetUserSuiteData(long userId) => Read(userId, state => state.Data);

    [HttpGet("metamiku/debug/getUserNotSuite/{userId}")]
    public IActionResult GetUserNotSuiteData(long userId) => Read(userId, state => state.Private);

    [HttpGet("metamiku/debug/getUserAllData/{userId}")]
    public IActionResult GetUserAllData(long userId) => Read(userId, state => new { suite = state.Data, notSuite = state.Private });

    private IActionResult Read(long userId, Func<UserState, object> select)
    {
        if (!ServerConfig.Debug)
            return StatusCode(403, "Debug mode is off");
        var snapshot = operations.Read(userId);
        if (snapshot == null)
            return NotFound("User not found");
        snapshot.Data.now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        return Content(JsonSerializer.Serialize(select(snapshot), DumpJson.Options), "application/json");
    }
}
