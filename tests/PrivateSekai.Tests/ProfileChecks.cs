extern alias game;

using System;
using System.IO;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class ProfileChecks
{
    public static MasterData Master()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "profile-fixture");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "configs.json"), """[{"configKey":"profile_word_max_length","value":"30"}]""");
        File.WriteAllText(Path.Combine(directory, "ngWords.json"), """[{"id":1,"word":"test"}]""");
        return new MasterData(new MasterCacheConfig { PinTables = [] }, directory);
    }

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
        var profiles = new ProfileService(user, thumbnails, Master());
        foreach (var item in new[] { (Type: "leader", Id: (int?)0, Status: 400),
            (Type: "card_before_special_training", Id: (int?)null, Status: 400),
            (Type: "card_before_special_training", Id: (int?)99, Status: 404),
            (Type: "card_after_special_training", Id: (int?)1, Status: 409) })
        {
            var bytes = operations.Execute(1, () =>
            {
                Check.That(profiles.UpdateProfile(new() { profileImageType = item.Type, profileImageId = item.Id,
                    word = "changed" }).Status == item.Status, "头像拒绝返回已核验的业务状态码");
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
            Check.That(profiles.UpdateProfile(new() { profileImageType = "card_after_special_training", profileImageId = 2 }).Status == 200,
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
        foreach (var item in new[] { (Word: new string('あ', 31), Status: 400, Error: ""),
            (Word: "test", Status: 409, Error: "contain_ng_word") })
            operations.Execute(1, () =>
            {
                Check.That(profiles.UpdateProfile(new() { profileImageType = "leader", word = item.Word, twitterId = "changed" }) ==
                    (item.Status, item.Error) && user.BuildRefresh().userProfile == null,
                    "超长与 NG 留言返回对应错误且不刷新资料");
                return null;
            });
        Check.That(store.Read(1)!.Data.userProfile.twitterId == null, "留言拒绝不修改同次社交 ID");
        operations.Execute(1, () =>
        {
            Check.That(profiles.UpdateProfile(new() { profileImageType = "leader", word = new string('あ', 30), twitterId = "fixture" }).Status == 200,
                "留言允许 master 长度边界");
            return user.BuildRefresh();
        });
        operations.Execute(1, () => { profiles.UpdateProfile(new() { profileImageType = "leader" }); return user.BuildRefresh(); });
        Check.That(store.Read(1)!.Data.userProfile.word == null && store.Read(1)!.Data.userProfile.twitterId == null,
            "省略留言与社交 ID 会清除原文本");
        foreach (var item in new[] {
            (Type: "leader", Id: 1, Word: "test", Status: 400),
            (Type: "card_before_special_training", Id: 99, Word: new string('あ', 31), Status: 404),
            (Type: "card_after_special_training", Id: 1, Word: "test", Status: 409),
            (Type: "leader", Id: (int?)null, Word: new string('あ', 30) + "test", Status: 400) })
            operations.Execute(1, () =>
            {
                Check.That(profiles.UpdateProfile(new() { profileImageType = item.Type, profileImageId = item.Id, word = item.Word }) ==
                    (item.Status, "") && user.BuildRefresh().userProfile == null,
                    "头像校验先于留言，长度校验先于 NG，拒绝时不刷新");
                return null;
            });
        operations.Execute(1, () =>
        {
            Check.That(profiles.UpdateProfile(new() { profileImageType = "leader", word = "あtestあ" }) == (409, "contain_ng_word"),
                "NG 词在留言中按子串匹配");
            return user.BuildRefresh();
        });
        var emptyText = operations.Execute(1, () =>
        {
            profiles.UpdateProfile(new() { profileImageType = "leader", word = "", twitterId = "" });
            return user.BuildRefresh();
        });
        var emptyProfile = DumpSerializer.Deserialize<SuiteUser>(emptyText).userProfile;
        Check.That(emptyProfile.word == "" && emptyProfile.twitterId == "", "显式空字符串保留文本字段");
    }
}
