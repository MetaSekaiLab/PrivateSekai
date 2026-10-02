extern alias game;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using game::Sekai;
using PrivateSekai.Config;
using PrivateSekai.Protocol;

namespace PrivateSekai.Services;


public class UserManager
{
    private static readonly JsonSerializerOptions JsonOpts = DumpJson.Options;

    private readonly Dictionary<long, GameUser> _users = new();
    private readonly ILogger<UserManager> _logger;
    
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>模板数据（对应 data/template/api_user_auth.json）</summary>
    private static UserAuthResponse ApiUserAuth { get; set; } = null!;

    public static UserAuthResponse GetApiUserAuth(string sessionToken)
    {
        ApiUserAuth.sessionToken = sessionToken;
        return ApiUserAuth;
    }

    /// <summary>模板数据（对应 data/template/api_system.json）</summary>
    private static SystemFullResponse ApiSystem { get; set; } = null!;

    public static SystemFullResponse GetApiSystem()
    {
        ApiSystem.serverDate = Now;
        return ApiSystem;
    }
    
    /// <summary>自定义补丁回调</summary>
    public Action<GameUser>? UserCustomizePatch => (user) =>
    {
        // 默认补丁，用于测试
        if (user.Data.userChargedCurrency != null)
            user.Data.userChargedCurrency.paid = 20070831;
        SetUserMaterialQuantity(user, 13, 20070831);
        
        
        if (ServerConfig.SkipTutorial)
        {
            user.Data.userTutorial = new UserTutorial
            {
                tutorialStatus = "end",
                tutorialEndAt = Now
            };
        }
    };

    public UserManager(ILogger<UserManager> logger)
    {
        _logger = logger;
        LoadTemplates();
        CreateUser0();
    }

    /// <summary>
    /// 加载模板数据: api_user_auth.json 和 api_system.json
    /// </summary>
    private void LoadTemplates()
    {
        var basePath = ServerConfig.TemplatePath;

        var authPath = Path.Combine(basePath, "api_user_auth.json");
        ApiUserAuth = JsonSerializer.Deserialize<UserAuthResponse>(
            File.ReadAllText(authPath), JsonOpts)!;

        var sysPath = Path.Combine(basePath, "api_system.json");
        ApiSystem = JsonSerializer.Deserialize<SystemFullResponse>(
            File.ReadAllText(sysPath), JsonOpts)!;
    }

    /// <summary>
    /// 从 user_0.json 创建模板用户 0
    /// </summary>
    private void CreateUser0()
    {
        var templatePath = Path.Combine(ServerConfig.TemplatePath, "user_0.json");
        var json = File.ReadAllText(templatePath);
        var data = JsonSerializer.Deserialize<SuiteUser>(json, JsonOpts)!;
        var user = new GameUser(data);
        
        user.EnsureShopAreaActionSets();
        UserCustomizePatch?.Invoke(user);
        
        _users[0] = user;
    }
    
    public GameUser GetUser(long userId)
    {
        if (userId == 0 && !UserExists(0))
            throw new Exception("Template user 0 not found");

        if (!UserExists(userId))
        {
            _logger.LogWarning($"User {userId} not found, forking new user");
            ForkNewUser(userId);
        }
        return _users[userId];
    }
    
    internal bool UserExists(long userId) => _users.ContainsKey(userId);

    public long GetNewUserId() =>
        _users.Keys.DefaultIfEmpty(-1).Max() + 1;

    public long ForkNewUser(long? userId = null)
    {
        var newUserId = userId ?? GetNewUserId();
        var fromUser = GetUser(0);
        var newUser = fromUser.DeepClone();

        newUser.InitAllUserId(newUserId);
        newUser.InitAllUserTime(Now);

        _users[newUserId] = newUser;
        _logger.LogInformation("New user forked: ID={UserId}", newUserId);
        return newUserId;
    }

    public string GetUserListJson()
    {
        var list = _users.Keys.Select(id => new { userId = id });
        return JsonSerializer.Serialize(list);
    }

    public IEnumerable<(long UserId, GameUser User)> GetAllUsers()
    {
        foreach (var kv in _users)
            yield return (kv.Key, kv.Value);
    }
    
    
    
    private static void SetUserMaterialQuantity(GameUser user, int materialId, int quantity)
    {
        user.Data.userMaterials ??= [];
        var materials = user.Data.userMaterials.ToList();
        var material = materials.FirstOrDefault(m => m.materialId == materialId);
        if (material == null)
        {
            material = new UserMaterial
            {
                materialId = materialId
            };
            materials.Add(material);
        }

        material.quantity = quantity;
        user.Data.userMaterials = materials.OrderBy(m => m.materialId).ToArray();
    }
}

