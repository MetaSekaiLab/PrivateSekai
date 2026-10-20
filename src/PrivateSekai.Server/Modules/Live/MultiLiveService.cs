extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Models;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Live;

public sealed class MultiLiveService(UserSession user, LiveMasterQueries master)
{
    public void Prepare(string liveId, MultiLiveRequest all, UserMultiLiveRequest request)
    {
        if (request.privateRoomSettings?.liveRuleType != "normal" || request.boostCount != 0
            || request.customMusicScoreId != null || request.musicCategoryName != "image")
            throw new MultiLiveRequestException(501);
        var settings = request.privateRoomSettings;
        if (settings.scoreCalculateType != null || settings.musicSelectionType != null || settings.musicDifficultyTypes != null
            || settings.deckTotalPowerRangeLower.HasValue || settings.deckTotalPowerRangeUpper.HasValue)
            throw new MultiLiveRequestException(501);
        if (master.GetMasterMusicDifficulty(request.musicDifficultyId)?.musicId != request.musicId
            || !master.IsMusicVideoSelection(request.musicId, request.musicVocalId, request.musicCategoryName))
            throw new MultiLiveRequestException(404);
        if (master.IsLimitedMusicOutOfTerm(request.musicId, user.Now))
            throw new MultiLiveRequestException(400, "limited_time_music_out_of_term");
        if (master.IsAprilFoolVocal(request.musicVocalId) && !master.IsAprilFoolSeason(user.Now))
            throw new MultiLiveRequestException(409, "out_of_period_2022_april_fool");
        var deck = (user.Data.userDecks ?? []).SingleOrDefault(d => d.deckId == request.deckId);
        if (deck == null) throw new MultiLiveRequestException(404);
        var cards = new[] { deck.member1, deck.member2, deck.member3, deck.member4, deck.member5 };
        if (cards.Any(id => id <= 0 || !(user.Data.userCards ?? []).Any(c => c.cardId == id))
            || cards.Distinct().Count() != 5 || !cards.Contains(deck.leader))
            throw new MultiLiveRequestException(409);
        user.Private.MultiLiveSessions.Clear();
        user.Private.MultiLiveSessions.Add(liveId, new() { Master = all, Start = request, CreatedAt = user.Now });
        user.NormalizeEventBreakTime();
        user.MarkChanged(nameof(SuiteUser.userEventBreakTime));
    }
}

public sealed class MultiLiveRequestException(int status, string errorCode = "") : Exception("多人演出准备请求被拒绝。")
{
    public int Status { get; } = status;
    public string ErrorCode { get; } = errorCode;
}
