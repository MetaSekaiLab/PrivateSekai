using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Config;
using PrivateSekai.Services;

namespace PrivateSekai.Controllers;

[ApiController]
public class DebugController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true };

    private readonly UserManager _users;

    public DebugController(UserManager users)
    {
        _users = users;
    }

    [HttpGet("favicon.ico")]
    public IActionResult Favicon() => NoContent();

    [HttpGet("metamiku/debug/getUserList")]
    public IActionResult GetUserList()
    {
        if (!ServerConfig.Debug)
            return StatusCode(403, "Debug mode is off");

        return Content(_users.GetUserListJson(), "application/json");
    }

    [HttpGet("metamiku/debug/getUserSuiteData/{userId}")]
    public IActionResult GetUserSuiteData(long userId)
    {
        if (!ServerConfig.Debug)
            return StatusCode(403, "Debug mode is off");

        if (!_users.UserExists(userId))
            return NotFound("User not found");

        var user = _users.GetUser(userId);
        var data = user.GetSuiteUserData();
        return Content(JsonSerializer.Serialize(data, JsonOpts), "application/json");
    }

    [HttpGet("metamiku/debug/getUserNotSuite/{userId}")]
    public IActionResult GetUserNotSuite(long userId)
    {
        if (!ServerConfig.Debug)
            return StatusCode(403, "Debug mode is off");

        if (!_users.UserExists(userId))
            return NotFound("User not found");

        var user = _users.GetUser(userId);
        return Content(JsonSerializer.Serialize(user.NotSuite, JsonOpts), "application/json");
    }

    [HttpGet("metamiku/debug/getUserAllData/{userId}")]
    public IActionResult GetUserAllData(long userId)
    {
        if (!ServerConfig.Debug)
            return StatusCode(403, "Debug mode is off");

        if (!_users.UserExists(userId))
            return NotFound("User not found");

        var user = _users.GetUser(userId);
        var allData = new
        {
            suite = user.GetSuiteUserData(),
            notSuite = user.NotSuite
        };
        return Content(JsonSerializer.Serialize(allData, JsonOpts), "application/json");
    }
}
