extern alias game;

using System;
using System.IO;
using System.Linq;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class CostumeFixChecks
{
    public static void Run()
    {
        var store = new MemoryUserStore();
        var state = TestUsers.Create(1);
        state.Data.userFixCostumes = [];
        state.Data.userCharacterCostume3ds = [new() { characterId = 21, unit = "piapro", headCostume3dId = 1, hairCostume3dId = 2, bodyCostume3dId = 3 }];
        state.Data.userMaterials = [new() { materialId = 1, quantity = 99 }];
        state.Data.userChargedCurrency = new() { free = 100, paid = 20 };
        state.Data.userMissionStatuses = [];
        state.Data.userBeginnerMissionV2s = [];
        store.Save(1, state);
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        var other = store.Read(1)!.Data;
        other.userFixCostumes = null;
        var otherBytes = DumpSerializer.Serialize(other);
        using var provider = TestUsers.Provider(store);
        using var scope = provider.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        // 固定设置不访问 master、任务或资源；正式依赖由 HTTP 检查覆盖。
        var service = new CostumeService(user, null!, null!, null!);
        (int Status, UserFixCostume[]? Rows) Change(Func<int> change)
        {
            var status = 0;
            var bytes = operation.Execute(1, () =>
            {
                status = change();
                return status == 200 ? (object)new SuiteUserCommonResponse { updatedResources = user.BuildRefresh() }
                    : new ClientErrorResponse { HttpStatus = (uint)status, ErrorCode = "", ErrorMessage = "" };
            });
            return (status, status == 200 ? DumpSerializer.Deserialize<SuiteUserCommonResponse>(bytes).updatedResources.userFixCostumes : null);
        }
        var first = Change(() => service.Fix(21, "piapro"));
        Check.That(first.Status == 200 && first.Rows!.Single().userId == 1 && first.Rows[0].unit == "piapro",
            "固定设置保存所属用户、角色与组合，不复制服装编号");
        Check.That(Change(() => service.Fix(21, "piapro")).Rows!.Length == 1, "重复固定仍返回完整列表且不重复插入");
        Check.That(Change(() => service.Fix(21, "light_sound")).Rows!.Single().unit == "light_sound", "切换组合替换同角色旧记录");
        Change(() => service.Fix(22, "idol"));
        var rows = Change(() => service.Fix(1, "piapro")).Rows!;
        Check.That(rows.Select(c => c.characterId).SequenceEqual(new[] { 1, 21, 22 }) && rows.All(c => c.userId == 1),
            "不同角色独立保存，按角色编号排序，未配置该组合服装也可固定");
        Change(() => service.Fix(0, "piapro"));
        Check.That(Change(() => service.Fix(int.MaxValue, "piapro")).Rows!.Select(c => c.characterId)
            .SequenceEqual(new[] { 0, 1, 21, 22, int.MaxValue }), "固定接口按官方行为接受零及未知角色，不增加master存在校验");
        Check.That(Change(() => service.Fix(21, "none")).Rows!.Single(c => c.characterId == 21).unit == "none", "none组合按请求保存");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.That(Change(() => service.Fix(21, null)).Status == 400 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
            "缺失组合整体拒绝，不覆盖原固定记录");
        Check.That(Change(() => service.Unfix(25)).Status == 404 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
            "取消未固定角色404且不改其他角色");
        foreach (var change in new Func<int>[] { () => service.Fix(21, "school_refusal"), () => service.Unfix(1) })
        {
            Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
            {
                Check.That(change() == 200, "回滚检查实际修改或移除固定设置");
                return new BrokenResponse();
            }), "固定与取消的响应编码失败回滚");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败保留完整固定列表");
        }
        var directory = Path.Combine(AppContext.BaseDirectory, "costume-fix-fixture", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "users.db");
        using (var sql = new SqliteUserStore(path)) sql.Save(1, store.Read(1)!);
        using (var sql = new SqliteUserStore(path))
            Check.That(sql.Read(1)!.Data.userFixCostumes.Length == 5 && sql.Read(1)!.Data.userFixCostumes.All(c => c.userId == 1),
                "SQLite重启保留固定组合与所属用户");
        foreach (var id in new[] { 0, 1, 21, 22, int.MaxValue }) Change(() => service.Unfix(id));
        Check.That(store.Read(1)!.Data.userFixCostumes.Length == 0 && Change(() => service.Unfix(21)).Status == 404,
            "最后一项取消后保存空列表，重复取消404");
        var after = store.Read(1)!.Data;
        after.userFixCostumes = null;
        Check.That(DumpSerializer.Serialize(after).SequenceEqual(otherBytes) && DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer),
            "固定及取消不改服装、材料、钱包、任务或其他账号");
    }
}
