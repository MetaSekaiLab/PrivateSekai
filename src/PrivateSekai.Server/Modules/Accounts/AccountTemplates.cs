extern alias game;

using System.IO;
using System.Linq;
using System.Text.Json;
using game::Sekai;
using PrivateSekai.Config;
using PrivateSekai.Transport;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Accounts;

/// <summary>模板保存为不可变字节，每次读取生成独立对象。</summary>
public sealed class AccountTemplates
{
    private readonly byte[] _user;
    private readonly byte[] _auth;
    private readonly byte[] _system;

    public AccountTemplates()
    {
        _auth = Load<UserAuthResponse>("api_user_auth.json");
        _system = Load<SystemFullResponse>("api_system.json");
        var data = DumpSerializer.Deserialize<SuiteUser>(Load<SuiteUser>("user_0.json"));
        // 保留现有本地测试模板的资源设置。
        if (data.userChargedCurrency != null)
            data.userChargedCurrency.paid = 20070831;
        var materials = (data.userMaterials ?? []).ToList();
        var material = materials.FirstOrDefault(m => m.materialId == 13);
        if (material == null)
        {
            material = new UserMaterial { materialId = 13 };
            materials.Add(material);
        }
        material.quantity = 20070831;
        data.userMaterials = materials.OrderBy(m => m.materialId).ToArray();
        _user = DumpSerializer.Serialize(data);
    }

    public UserAuthResponse GetAuth(string sessionToken)
    {
        var response = DumpSerializer.Deserialize<UserAuthResponse>(_auth);
        response.sessionToken = sessionToken;
        return response;
    }

    public SystemFullResponse GetSystem(long now)
    {
        var response = DumpSerializer.Deserialize<SystemFullResponse>(_system);
        response.serverDate = now;
        return response;
    }

    public UserState CreateUser(long userId, long now)
    {
        var data = DumpSerializer.Deserialize<SuiteUser>(_user);
        if (data.userRegistration != null)
        {
            data.userRegistration.userId = userId;
            data.userRegistration.signature = JwtSignature.GenUserSignature(userId);
            data.userRegistration.registeredAt = (ulong)now;
        }
        if (data.userGamedata != null) data.userGamedata.userId = userId;
        if (data.userCards != null)
            foreach (var card in data.userCards) { card.userId = userId; card.createdAt = now; }
        if (data.userDecks != null)
            foreach (var deck in data.userDecks) deck.userId = userId;
        if (data.userUnits != null)
            foreach (var unit in data.userUnits) unit.userId = userId;
        if (data.unreadUserTopics != null)
            foreach (var topic in data.unreadUserTopics) topic.userId = userId;
        if (data.userMaterialExchanges != null)
            foreach (var item in data.userMaterialExchanges) item.userId = userId;
        if (data.userGachaCeilExchanges != null)
            foreach (var item in data.userGachaCeilExchanges) item.userId = userId;
        if (data.userCharacterMissionStatuses != null)
            foreach (var status in data.userCharacterMissionStatuses) status.userId = userId;
        if (data.userCostume3dStatuses != null)
            foreach (var status in data.userCostume3dStatuses) status.obtainedAt = now;
        if (data.userReleaseConditions != null)
            foreach (var condition in data.userReleaseConditions) condition.createdAt = now;
        data.now = now;
        data.userBoost?.recoveryAt = (ulong)now;
        if (ServerConfig.SkipTutorial)
            data.userTutorial = new UserTutorial { tutorialStatus = "end", tutorialEndAt = now };
        return new UserState { Data = data };
    }

    private static byte[] Load<T>(string file) => DumpSerializer.Serialize(
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(ServerConfig.TemplatePath, file)), DumpJson.Options)!);
}
