extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Live;

public sealed class DeckService(UserSession user)
{
    public static bool IsNameTooLong(string? name) => name?.Length > 10;

    public void Save(PutUserDeckRequest request)
    {
        if (request.userWorldBloomSupportDeckUpdates is { Length: > 0 })
            throw new NotSupportedException("World Bloom support deck updates are not implemented.");
        var updates = request.userDeckUpdates ?? [];
        if (updates.Any(u => u?.userDeck == null || u.userDeck.deckId <= 0) ||
            updates.Select(u => u.userDeck.deckId).Distinct().Count() != updates.Length)
            throw new ArgumentException("Invalid or duplicate deck update.");
        var decks = (user.Data.userDecks ?? []).ToList();
        var owned = (user.Data.userCards ?? []).Select(c => c.cardId).ToHashSet();
        foreach (var update in updates)
        {
            var deck = update.userDeck;
            if (IsNameTooLong(deck.name))
                throw new ArgumentException("Deck name exceeds the maximum length.");
            if (deck.userId != 0 && deck.userId != user.UserId)
                throw new ArgumentException("Deck belongs to another user.");
            if (!update.isDeleted)
            {
                int[] members = [deck.member1, deck.member2, deck.member3, deck.member4, deck.member5];
                var cards = members.Where(id => id != 0).ToArray();
                if (members.Any(id => id < 0) || cards.Any(id => !owned.Contains(id)) || cards.Distinct().Count() != cards.Length ||
                    (deck.leader != 0 && !cards.Contains(deck.leader)) ||
                    (deck.subLeader != 0 && !cards.Contains(deck.subLeader)) ||
                    (deck.subLeader != 0 && deck.subLeader == deck.leader))
                    throw new ArgumentException("Invalid deck members or leaders.");
            }
            decks.RemoveAll(d => d.deckId == deck.deckId);
            if (!update.isDeleted)
                decks.Add(new UserDeck
                {
                    userId = user.UserId, deckId = deck.deckId, name = deck.name,
                    leader = deck.leader, subLeader = deck.subLeader,
                    member1 = deck.member1, member2 = deck.member2, member3 = deck.member3,
                    member4 = deck.member4, member5 = deck.member5
                });
        }
        var gamedata = user.Data.userGamedata ?? throw new InvalidOperationException("Missing user game data.");
        var mainDeckId = request.mainDeckId ?? gamedata.deck;
        if (!decks.Any(d => d.deckId == mainDeckId))
            throw new ArgumentException("Main deck must exist after updates.");
        if (updates.Length > 0)
        {
            user.Data.userDecks = decks.OrderBy(d => d.deckId).ToArray();
            user.MarkChanged(nameof(SuiteUser.userDecks));
        }
        if (request.mainDeckId.HasValue)
        {
            gamedata.deck = mainDeckId;
            user.MarkChanged(nameof(SuiteUser.userGamedata));
        }
    }
}
