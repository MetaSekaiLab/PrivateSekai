extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using MessagePack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Modules.Accounts;
using PrivateSekai.Modules.Home;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class LoginStatusChecks
{
    public static void Run()
    {
        var store = new MemoryUserStore();
        var writer = TestUsers.Create(1);
        writer.Data.userConfig = new UserConfig { isDisplayLoginStatus = true };
        writer.Data.userProfile = new UserProfile { profileImageType = "leader" };
        writer.Data.userGamedata.deck = 1;
        writer.Data.userDecks = [new() { deckId = 1, leader = 1 }];
        writer.Data.userCards = [new UserCard { cardId = 1 }];
        var viewer = TestUsers.Create(2);
        viewer.Data.userFriends = [new UserFriend { opponentUserId = 1, friendStatus = "friend" }];
        store.Save(1, writer);
        store.Save(2, viewer);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var home = new HomeService(user);
        var status = new LoginStatusController(operations, home);
        var bytes = ((FileContentResult)status.Save(1, new PutUserLoginStatusRequest { loginStatus = "solo_live" })).FileContents;
        Check.That(MessagePackSerializer.ConvertToJson(bytes) == "{}", "在线状态写入返回空 map");
        Check.That(store.Read(1)!.Private.LoginStatus?.loginStatus == "solo_live", "在线状态保存到私有数据");
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            home.SetLoginStatus("offline");
            return new BrokenResponse();
        }), "在线状态编码失败回滚");
        Check.That(store.Read(1)!.Private.LoginStatus?.loginStatus == "solo_live", "失败不覆盖已有状态");
        var controller = new LoginController(operations, user, null!, home, null!, new FriendQueries(operations));
        JsonObject ReadFriend()
        {
            var result = (FileContentResult)controller.HandleSuiteUserParts(2, ["user_friend"]);
            return JsonNode.Parse(MessagePackSerializer.ConvertToJson(result.FileContents))!["userFriends"]![0]!["userLoginStatus"]!.AsObject();
        }
        var visible = ReadFriend();
        Check.That(visible["loginStatus"]!.GetValue<string>() == "solo_live" && visible.ContainsKey("loginStatusUpdatedAt"),
            "另一用户读取当前状态和时间戳");
        Check.That(store.Read(2)!.Private.LoginStatus?.loginStatus == "online" &&
            store.Read(1)!.Private.LoginStatus?.loginStatus == "solo_live", "parts 只把当前用户标记在线");
        var hidden = store.Read(1)!;
        hidden.Data.userConfig.isDisplayLoginStatus = false;
        store.Save(1, hidden);
        var invisible = ReadFriend();
        Check.That(invisible.Count == 1 && invisible["loginStatus"]!.GetValue<string>() == "offline", "隐藏在线时省略时间戳");
        Check.That(store.Read(1)!.Private.LoginStatus?.loginStatus == "solo_live", "隐藏仅影响输出，不覆盖真实状态");
        var copy = store.Read(1)!;
        copy.Private.LoginStatus!.loginStatus = "offline";
        Check.That(store.Read(1)!.Private.LoginStatus!.loginStatus == "solo_live", "私有状态读取隔离可变引用");
    }
}
