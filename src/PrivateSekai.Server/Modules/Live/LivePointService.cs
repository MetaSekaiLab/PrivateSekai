extern alias game;

using System;
using game::Sekai;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Live;

public sealed class LivePointService(UserSession user, LiveMasterQueries master, MissionMasterQueries missions)
{
    private const int DailyLimit = 3;
    private const int DailyBonus = 10;

    public UserLivePoint Build(MasterBoost boost, bool multiClear = false)
    {
        var hour = master.GetDayChangeHour();
        if (hour is < 0 or > 23) throw new InvalidOperationException("Invalid game day change hour.");
        var local = DateTimeOffset.FromUnixTimeMilliseconds(user.Now).ToOffset(TimeSpan.FromHours(9)).AddHours(-hour);
        var day = DateOnly.FromDateTime(local.DateTime).DayNumber;
        var saved = user.Private.DailyLivePoint;
        var count = saved.Day == day ? saved.BonusCount : 0;
        if (count is < 0 or > DailyLimit) throw new InvalidOperationException("Invalid daily Live point count.");
        var bonus = multiClear && count < DailyLimit;
        if (bonus)
        {
            count++;
            saved.Day = day;
            saved.BonusCount = count;
        }
        return new()
        {
            addNormalProgress = boost.livePointRate, addDailyBonusProgress = bonus ? DailyBonus : 0,
            livePointBonusRemaining = DailyLimit - count, liveMissionPeriodId = missions.GetLiveMissionPeriodId(user.Now)
        };
    }
}
