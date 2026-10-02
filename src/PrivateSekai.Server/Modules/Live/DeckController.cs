extern alias game;

using System.Collections.Generic;
using game::Sekai;
using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Live;

public sealed class DeckController(UserOperation operations, UserSession user, DeckService decks) : PrskController
{
    [HttpPut("api/user/{userId}/deck")]
    public IActionResult Save(long userId, [FromBody] PutUserDeckRequest request)
    {
        var errors = new Dictionary<string, string>();
        var updates = request.userDeckUpdates ?? [];
        for (var i = 0; i < updates.Length; i++)
            if (DeckService.IsNameTooLong(updates[i]?.userDeck?.name))
                errors[$"userDeckUpdates[{i}].userDeck.name"] = "Length";
        if (errors.Count > 0)
        {
            Response.StatusCode = 400;
            return Encoded(DumpSerializer.Serialize(errors));
        }
        return Encoded(operations.Execute(userId, () =>
        {
            decks.Save(request);
            return new UserDeckResponse { updatedResources = user.BuildRefresh() };
        }));
    }
}
