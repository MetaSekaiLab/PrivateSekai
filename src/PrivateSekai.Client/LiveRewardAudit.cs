using System.Text.Json.Nodes;

namespace PrivateSekai.Client;

public static class LiveRewardAudit
{
    public static JsonObject Analyze(JsonObject record, JsonArray boxes)
    {
        if (record["operation"]?.GetValue<string>() != "live-clear" ||
            record["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要成功结算及其前后快照。");
        var response = record["response"]!.AsObject();
        var scoreRewards = response["scoreRankRewards"]!.AsArray();
        var rate = response["boost"]!["rewardRate"]!.GetValue<int>();
        if (rate <= 0) throw new InvalidOperationException("奖励倍率必须为正数。");
        var rewards = new List<JsonNode>();
        foreach (var field in new[] { "scoreRankRewards", "musicAchievementRewards", "playerRankRewards" })
            rewards.AddRange(response[field]!.AsArray().Select(r => r!));
        foreach (var limited in response["limitedTermScoreRankRewards"]!.AsArray())
            rewards.AddRange(limited!["obtainedRewards"]!.AsArray().Select(r => r!));
        if (rewards.Any(r => r["quantity"] == null || Number(r, "quantity") <= 0))
            throw new InvalidOperationException("奖励缺少正数数量。");

        var candidates = new JsonArray();
        foreach (var reward in scoreRewards)
        {
            var ids = new JsonArray();
            foreach (var box in boxes.Where(b => b?["resourceBoxPurpose"]?.GetValue<string>() == "score_rank_reward_detail"))
            {
                if (box!["resourceBoxType"]?.GetValue<string>() != "expand" || box["details"] is not JsonArray { Count: 1 } details)
                    continue;
                var detail = details[0]!;
                if (Key(detail) == Key(reward!) && Number(detail, "resourceLevel") == Number(reward!, "resourceLevel") &&
                    checked(Number(detail, "resourceQuantity") * rate) == Number(reward!, "quantity"))
                    ids.Add(box["id"]!.GetValue<int>());
            }
            candidates.Add(new JsonObject
            {
                ["resourceType"] = Key(reward!).Type, ["resourceId"] = Key(reward!).Id,
                ["quantity"] = Number(reward!, "quantity"), ["candidateBoxIds"] = ids
            });
        }

        var before = Balances(record["before"]!);
        var after = Balances(record["after"]!);
        var grants = rewards.GroupBy(Key).ToDictionary(g => g.Key, g => g.Sum(r => Number(r, "quantity")));
        var unsupported = grants.Keys.Where(k => k.Type is not ("coin" or "material" or "practice_ticket"))
            .Select(k => k.Type).Distinct().Order(StringComparer.Ordinal).ToArray();
        var differences = new JsonArray();
        foreach (var key in before.Keys.Union(after.Keys).Union(grants.Keys)
                     .Where(k => k.Type is "coin" or "material" or "practice_ticket").OrderBy(k => k.Type).ThenBy(k => k.Id))
        {
            var actual = after.GetValueOrDefault(key) - before.GetValueOrDefault(key);
            var expected = grants.GetValueOrDefault(key);
            if (actual != expected)
                differences.Add(new JsonObject
                {
                    ["resourceType"] = key.Type, ["resourceId"] = key.Id,
                    ["expectedIncrease"] = expected, ["actualIncrease"] = actual
                });
        }
        return new JsonObject
        {
            ["rewardRate"] = rate, ["scoreRewardCandidates"] = candidates,
            ["balanceDifferences"] = differences,
            ["unsupportedResourceTypes"] = new JsonArray(unsupported.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["balancesVerified"] = differences.Count == 0 && unsupported.Length == 0
        };
    }

    private static (string Type, int Id) Key(JsonNode resource) =>
        (resource["resourceType"]!.GetValue<string>(), (int)Number(resource, "resourceId"));

    private static long Number(JsonNode resource, string field) => resource[field]?.GetValue<long>() ?? 0;

    private static Dictionary<(string Type, int Id), long> Balances(JsonNode suite)
    {
        var result = new Dictionary<(string Type, int Id), long>
        {
            [("coin", 0)] = suite["userGamedata"]!["coin"]!.GetValue<long>()
        };
        foreach (var (field, type, id) in new[]
                 { ("userMaterials", "material", "materialId"), ("userPracticeTickets", "practice_ticket", "practiceTicketId") })
        {
            if (suite[field] is not JsonArray items) throw new InvalidOperationException("结算快照缺少资源数组。");
            foreach (var item in items) result.Add((type, item![id]!.GetValue<int>()), item["quantity"]!.GetValue<long>());
        }
        return result;
    }
}
