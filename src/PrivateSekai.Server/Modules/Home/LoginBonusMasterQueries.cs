extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Home;

public sealed class LoginBonusMasterQueries(MasterData master)
{
    public (string Type, int Id, int BoxId, string Reason)[] InitialRewards(long now)
    {
        var normalId = int.Parse(master.GetTable<MasterConfig>("configs").Rows
            .Single(c => c.configKey == "login_bonus_id").value);
        var normal = master.GetTable<MasterLoginBonus>("loginBonuses").Rows.Single(b => b.day == 1);
        var result = new List<(string, int, int, string)>
        {
            ("normal", normalId, normal.resourceBoxId, "ログインボーナスの報酬です。")
        };
        if (master.GetTable<MasterConfig>("configs").Rows
            .Single(c => c.configKey == "beginner_login_bonus_enable").value == "true")
        {
            var summary = master.GetTable<MasterBeginnerLoginBonusSummary>("beginnerLoginBonusSummaries").Rows
                .Single(s => s.startAt <= now && now < s.endAt);
            if (summary.loginBonusId != 3)
                throw new NotSupportedException("Beginner login reward wording is not verified.");
            var beginner = master.GetTable<MasterBeginnerLoginBonus>("beginnerLoginBonuses").Rows
                .Single(b => b.loginBonusId == summary.loginBonusId && b.day == 1);
            result.Add(("beginner", summary.loginBonusId, beginner.resourceBoxId, "初心者応援ログインキャンペーン1日目の報酬です。"));
        }
        foreach (var limited in master.GetTable<MasterLimitedLoginBonus>("limitedLoginBonuses").Rows
            .Where(b => b.startAt <= now && now < b.closeAt && now < b.endAt).OrderBy(b => b.seq))
        {
            // 只使用已核验的赠礼文案形式，其他活动不套用天数规则。
            var suffix = limited.id switch
            {
                225 => "1日目の報酬です。",
                226 => "の報酬です。",
                _ => throw new NotSupportedException("Limited login reward wording is not verified.")
            };
            result.Add(("limited", limited.id, limited.limitedLoginBonusDetails.Single(d => d.day == 1).resourceBoxId,
                limited.name + suffix));
        }
        return result.ToArray();
    }
}
