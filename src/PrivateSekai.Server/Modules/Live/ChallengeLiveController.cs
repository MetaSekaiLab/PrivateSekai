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
