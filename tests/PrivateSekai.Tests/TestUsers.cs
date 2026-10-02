extern alias game;

using System;
using game::Sekai;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class TestUsers
{
    public static UserState Create(long id, int coin = 100)
    {
        var data = new SuiteUser
        {
            userRegistration = new() { userId = id },
            userGamedata = new UserGamedata { userId = id, coin = coin, name = "fixture" },
            userCards = [],
            userMaterials = [],
            userPresents = [],
            refreshableTypes = []
        };
        return new UserState { Data = data };
    }

    public static ServiceProvider Provider(IUserStore? store = null, UserLocks? locks = null) =>
        new ServiceCollection()
            .AddSingleton<IUserStore>(store ?? new MemoryUserStore())
            .AddSingleton(locks ?? new UserLocks())
            .AddSingleton<TimeProvider>(new FixedTimeProvider())
            .AddScoped<UserSession>()
            .AddScoped<UserOperation>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
