extern alias game;

using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class ProfileChecks
{
    public static void Run()
    {
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userProfile = new() { userId = 1, profileImageType = "leader", word = "before" };
        state.Data.userCards = [new() { cardId = 1, specialTrainingStatus = "not_doing" },
            new() { cardId = 2, specialTrainingStatus = "done" }];
        store.Save(1, state);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        using var thumbnails = new CustomProfileThumbnailStore();
        var profiles = new ProfileService(user, thumbnails);
        foreach (var item in new[] { (Type: "leader", Id: (int?)0, Status: 400),
            (Type: "card_before_special_training", Id: (int?)null, Status: 400),
            (Type: "card_before_special_training", Id: (int?)99, Status: 404),
            (Type: "card_after_special_training", Id: (int?)1, Status: 409) })
        {
            var bytes = operations.Execute(1, () =>
            {
                Check.That(profiles.UpdateProfile(new() { profileImageType = item.Type, profileImageId = item.Id,
                    word = "changed" }) == item.Status, "头像拒绝返回已核验的业务状态码");
                return user.BuildRefresh();
            });
            Check.That(DumpSerializer.Deserialize<SuiteUser>(bytes).userProfile == null &&
                store.Read(1)!.Data.userProfile.word == "before", "头像拒绝不刷新资料，也不修改同一请求的留言");
        }
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            profiles.UpdateProfile(new() { profileImageType = "card_after_special_training", profileImageId = 2 });
            return new BrokenResponse();
        }), "头像保存编码失败回滚");
        Check.That(store.Read(1)!.Data.userProfile.profileImageType == "leader" &&
            store.Read(1)!.Data.userProfile.word == "before", "回滚保留原头像及留言");
        operations.Execute(1, () =>
        {
            Check.That(profiles.UpdateProfile(new() { profileImageType = "card_after_special_training", profileImageId = 2 }) == 200,
                "已特训卡牌支持特训后头像");
            return user.BuildRefresh();
        });
        var restored = operations.Execute(1, () =>
        {
            profiles.UpdateProfile(new() { profileImageType = "leader" });
            return user.BuildRefresh();
        });
        var profile = DumpSerializer.Deserialize<SuiteUser>(restored).userProfile;
        Check.That(profile.userId == 1 && profile.profileImageType == "leader" && profile.profileImageId == 0,
            "恢复队长头像清除自选 ID 并保留账号 ID");
    }
}
