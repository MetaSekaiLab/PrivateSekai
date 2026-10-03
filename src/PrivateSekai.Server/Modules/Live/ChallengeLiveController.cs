extern alias game;

using System.Linq;
using game::Sekai;
using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;
using PrivateSekai.Transport;

namespace PrivateSekai.Modules.Live;

public sealed class ChallengeLiveController(UserOperation operations, UserSession user, ChallengeLiveService challenges) : PrskController
{
    [HttpPost("api/user/{userId}/challenge-live/solo")]
    public IActionResult Start(long userId, [FromBody] UserChallengeLiveStartRequest request)
    {
        var status = 200;
        var encoded = operations.Execute(userId, () =>
        {
            var result = challenges.Start(request);
            status = result.Status;
            if (result.Response != null) result.Response.updatedResources = user.BuildRefresh();
            return result.Response;
        });
        return status == 200 ? Encoded(encoded) : StatusCode(status);
    }

    [HttpPost("api/user/{userId}/challenge-live-character/{characterId}")]
    public IActionResult UnlockCharacter(long userId, int characterId)
    {
        var unlocked = false;
        var response = operations.Execute(userId, () =>
        {
            unlocked = challenges.TryUnlockFirstCharacter(characterId);
            return unlocked ? new UserChallengeLiveCharacterResponse { updatedResources = user.BuildRefresh() } : null;
        });
        return unlocked ? Encoded(response) : StatusCode(409);
    }

    [HttpPut("api/user/{userId}/challenge-live-solo-deck/{characterId}")]
    public IActionResult SaveDeck(long userId, int characterId, [FromBody] UserChallengeLiveSoloDeck request) =>
        Encoded(operations.Execute(userId, () =>
        {
            var deck = challenges.SaveDeck(characterId, request);
            var response = new UserChallengeLiveSoloDeckResponse { updatedResources = user.BuildRefresh() };
            DumpContract.For(typeof(UserChallengeLiveSoloDeckResponse)).Members
                .Single(m => (string)m.Key == "userChallengeLiveSoloDeck").Set(response, deck);
            return response;
        }));
}
