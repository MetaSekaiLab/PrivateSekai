extern alias game;

using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class UserConfigChecks
{
    public static void Run()
    {
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userConfig = new() { defaultMusicType = "sekai", isDisplayLoginStatus = true, friendRequestScope = "all" };
        store.Save(1, state);
        var other = TestUsers.Create(2);
        other.Data.userConfig = new() { defaultMusicType = "sekai", isDisplayLoginStatus = true, friendRequestScope = "all" };
        store.Save(2, other);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operations = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var profiles = new ProfileService(user, new CustomProfileThumbnailStore(), null!);
        var bytes = operations.Execute(1, () =>
        {
            profiles.SaveConfig(new() { isDisplayLoginStatus = false });
            return user.BuildRefresh();
        });
        var config = DumpSerializer.Deserialize<SuiteUser>(bytes).userConfig;
        Check.That(!config.isDisplayLoginStatus && config.defaultMusicType == "sekai" && config.friendRequestScope == "all",
            "false 更新在线显示，未指定字段保留并完整返回");
        var empty = operations.Execute(1, () => { profiles.SaveConfig(new()); return user.BuildRefresh(); });
        Check.That(DumpSerializer.Deserialize<SuiteUser>(empty).userConfig == null, "全 null 配置不刷新");
        var same = operations.Execute(1, () => { profiles.SaveConfig(new() { isDisplayLoginStatus = false }); return user.BuildRefresh(); });
        Check.That(DumpSerializer.Deserialize<SuiteUser>(same).userConfig != null, "相同非 null 配置仍刷新");
        Check.Throws<MessagePackSerializationException>(() => operations.Execute(1, () =>
        {
            profiles.SaveConfig(new() { defaultMusicType = "original_music", friendRequestScope = "reject" });
            return new BrokenResponse();
        }), "配置编码失败回滚");
        Check.That(store.Read(1)!.Data.userConfig.defaultMusicType == "sekai" &&
            store.Read(1)!.Data.userConfig.friendRequestScope == "all", "回滚保留两个字符串字段");
        Check.That(store.Read(2)!.Data.userConfig.isDisplayLoginStatus, "配置写入不影响其他用户");
    }
}
