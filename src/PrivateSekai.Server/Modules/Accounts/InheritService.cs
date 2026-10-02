extern alias game;

using System;
using game::Sekai;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Accounts;

public sealed class InheritService(UserSession user)
{
    public string SetUserInherit(string password)
    {
        var id = Random.Shared.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 16);
        user.Private.InheritId = id;
        user.Private.InheritPassword = password;
        user.Data.userInherit = new UserInherit { inheritId = id };
        user.MarkChanged(nameof(SuiteUser.userInherit));
        return id;
    }

    public static bool Matches(UserState state, string inheritId, string password) =>
        !string.IsNullOrEmpty(inheritId) && !string.IsNullOrEmpty(password) &&
        state.Private.InheritId == inheritId && state.Private.InheritPassword == password;

    public static UserGamedata GetPreview(UserState state)
    {
        var data = state.Data.userGamedata;
        return new UserGamedata { userId = data.userId, name = data.name, deck = data.deck, rank = data.rank };
    }
}
