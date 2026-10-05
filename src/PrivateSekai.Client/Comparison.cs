using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PrivateSekai.Client;

public sealed class Redactor
{
    private readonly HashSet<string> secrets = [];
    private readonly HashSet<long> accounts = [];
    public static bool IsSensitive(string name) => Regex.IsMatch(name,
        "credential|token|cookie|authorization|password|signature|deviceid|install.?id|email|phone|inherit", RegexOptions.IgnoreCase);
    public void AddSecret(string? value) { if (!string.IsNullOrEmpty(value)) secrets.Add(value); }
    public void AddAccount(long id) { if (id > 0) accounts.Add(id); }

    public JsonNode? Clean(JsonNode? node, string field = "")
    {
        if (node == null) return null;
        if (IsSensitive(field) || field is "userId" or "friendId" or "targetUserId" or "name" or "tabName" or "word" or "twitterId" or "text" or "thumbnail")
            return JsonValue.Create("<redacted>");
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var pair in obj) result[pair.Key] = Clean(pair.Value, pair.Key);
            return result;
        }
        if (node is JsonArray array) return new JsonArray(array.Select(n => Clean(n)).ToArray());
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                foreach (var secret in secrets.OrderByDescending(s => s.Length)) text = text.Replace(secret, "<redacted>", StringComparison.Ordinal);
                foreach (var account in accounts.Where(id => id > 9999999999)) text = text.Replace(account.ToString(CultureInfo.InvariantCulture), "<account>", StringComparison.Ordinal);
                text = Regex.Replace(text, @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "<redacted>");
                return JsonValue.Create(text);
            }
        }
        return node.DeepClone();
    }
}

public sealed record Difference(string Path, string Kind, JsonNode? Before, JsonNode? After, decimal? Delta);

public static class Comparison
{
    private static readonly Dictionary<string, string[]> IdentityKeys = new()
    {
        ["userCards"] = ["cardId"], ["userMaterials"] = ["materialId"],
        ["userMaterialExchanges"] = ["materialExchangeId"], ["userPracticeTickets"] = ["practiceTicketId"],
        ["userEventItems"] = ["eventItemId"], ["userEventExchanges"] = ["eventId", "eventExchangeId"],
        ["userEventMissions"] = ["eventId", "eventMissionId"],
        ["userBoostItems"] = ["boostItemId"],
        ["userStamps"] = ["stampId"],
        ["userMusics"] = ["musicId"], ["userMusicVocals"] = ["musicVocalId"],
        ["userMyLists"] = ["listNo"],
        ["userStampFavoriteTabs"] = ["tabNum"], ["userStampFavorites"] = ["tabNum", "num"],
        ["userSkillPracticeTickets"] = ["skillPracticeTicketId"], ["userDecks"] = ["deckId"],
        ["userShops"] = ["shopId"], ["userShopItems"] = ["shopItemId"], ["userAreaItems"] = ["areaItemId"],
        ["userStoryFavorites"] = ["shareNo"], ["userBookmarkedStories"] = ["storyType", "storyId"],
        ["userStoryEpisodeBookmarks"] = ["storyId", "storyEpisodeId", "talkId"],
        ["userMissionStatuses"] = ["missionType", "missionId"], ["userLiveMissions"] = ["liveMissionPeriodId"],
        ["userReleaseConditions"] = ["releaseConditionId"],
        ["userCharacterMissionV2s"] = ["characterId", "characterMissionType"],
        ["userCharacterMissionV2Statuses"] = ["characterId", "missionId", "parameterGroupId", "seq"],
        ["userUnitEpisodeStatuses"] = ["episodeId"], ["userSpecialEpisodeStatuses"] = ["episodeId"]
    };

    // 只消除明确的传输噪声；recoveryAt、createdAt 等业务时间保留。
    public static JsonNode? Normalize(JsonNode? node, string field = "")
    {
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                if (pair.Key is not ("now" or "serverDate" or "refreshableTypes"))
                    result[pair.Key] = Normalize(pair.Value, pair.Key);
            return result;
        }
        if (node is JsonArray array)
        {
            if (IdentityKeys.TryGetValue(field, out var keys) && array.All(n => n is JsonObject o && keys.All(o.ContainsKey)))
            {
                var ids = array.Select(n => string.Join(",", keys.Select(k => k + "=" + n![k]!.ToJsonString()))).ToArray();
                if (ids.Distinct(StringComparer.Ordinal).Count() == ids.Length)
                {
                    var result = new JsonObject();
                    for (var i = 0; i < ids.Length; i++) result[ids[i]] = Normalize(array[i]);
                    return result;
                }
            }
            return new JsonArray(array.Select(n => Normalize(n)).ToArray());
        }
        return node?.DeepClone();
    }

    public static List<Difference> Diff(JsonNode? before, JsonNode? after, string path = "")
    {
        if (JsonNode.DeepEquals(before, after)) return [];
        if (before is JsonObject left && after is JsonObject right)
        {
            var changes = new List<Difference>();
            foreach (var key in left.Select(p => p.Key).Union(right.Select(p => p.Key)).OrderBy(k => k, StringComparer.Ordinal))
            {
                var child = path + "/" + key.Replace("~", "~0").Replace("/", "~1");
                if (!left.ContainsKey(key)) changes.Add(new(child, "added", null, right[key]?.DeepClone(), null));
                else if (!right.ContainsKey(key)) changes.Add(new(child, "removed", left[key]?.DeepClone(), null, null));
                else changes.AddRange(Diff(left[key], right[key], child));
            }
            return changes;
        }
        decimal? delta = before is JsonValue a && after is JsonValue b && a.TryGetValue<decimal>(out var x) && b.TryGetValue<decimal>(out var y)
            ? y - x : null;
        return [new(path, "changed", before?.DeepClone(), after?.DeepClone(), delta)];
    }

    public static JsonObject DeltaView(IEnumerable<Difference> differences)
    {
        var view = new JsonObject();
        foreach (var item in differences)
            view[item.Path] = item.Delta.HasValue
                ? new JsonObject { ["kind"] = item.Kind, ["delta"] = item.Delta.Value }
                : new JsonObject { ["kind"] = item.Kind, ["before"] = item.Before?.DeepClone(), ["after"] = item.After?.DeepClone() };
        return view;
    }

    public static JsonNode? Resolve(JsonNode? node, string pointer)
    {
        if (pointer == "") return node;
        if (!pointer.StartsWith('/')) throw new InvalidOperationException("断言路径必须使用 JSON Pointer。");
        foreach (var segment in pointer[1..].Split('/'))
        {
            var key = segment.Replace("~1", "/").Replace("~0", "~");
            node = node switch
            {
                JsonObject o when o.ContainsKey(key) => o[key],
                JsonArray a when int.TryParse(key, out var i) && i >= 0 && i < a.Count => a[i],
                _ => throw new InvalidOperationException("断言路径不存在。")
            };
        }
        return node;
    }
}
