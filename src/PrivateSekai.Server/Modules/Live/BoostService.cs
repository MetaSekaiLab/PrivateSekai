extern alias game;

using System;
using game::Sekai;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Live;

public sealed class BoostService(UserSession user, LiveMasterQueries master)
{
    public Boost Preview()
    {
        var source = user.Data.userBoost ?? throw new InvalidOperationException("Boost data is missing.");
        var result = new Boost { current = source.current, recoveryAt = source.recoveryAt };
        if (user.Data.userColorfulPassV2?.colorfulPassId > 0) return result;
        var maximum = master.GetBoostConfig("boost_recovery_max_count");
        var seconds = master.GetBoostConfig("boost_recovery_second");
        if (maximum <= 0 || seconds <= 0 || result.current < 0)
            throw new InvalidOperationException("Invalid natural boost recovery state.");
        var now = checked((ulong)user.Now);
        if (result.recoveryAt > now) return result;
        if (result.current >= maximum)
        {
            result.recoveryAt = now;
            return result;
        }
        var interval = checked((ulong)seconds * 1000);
        var ticks = Math.Min((now - result.recoveryAt) / interval, (ulong)(maximum - result.current));
        result.current += (int)ticks;
        result.recoveryAt = result.current == maximum ? now : result.recoveryAt + ticks * interval;
        return result;
    }

    public void Normalize()
    {
        if (user.Data.userBoost == null) return;
        var updated = Preview();
        if (updated.current == user.Data.userBoost.current && updated.recoveryAt == user.Data.userBoost.recoveryAt) return;
        user.Data.userBoost = updated;
        user.MarkChanged(nameof(SuiteUser.userBoost));
    }

    public void Consume(int count)
    {
        if (user.Data.userBoost == null || count <= 0) return;
        Normalize();
        user.Data.userBoost.current = Math.Max(0, user.Data.userBoost.current - count);
        user.MarkChanged(nameof(SuiteUser.userBoost));
    }
}
