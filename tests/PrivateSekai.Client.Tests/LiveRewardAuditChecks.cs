using System.Text.Json.Nodes;
using PrivateSekai.Client;

internal static class LiveRewardAuditChecks
{
    public static void Run(Action<bool, string> check)
    {
        var record = JsonNode.Parse("""
        {"operation":"live-clear","status":"completed",
         "before":{"userGamedata":{"coin":100},"userChargedCurrency":{"free":50,"paid":10},"userMaterials":[],"userPracticeTickets":[]},
         "after":{"userGamedata":{"coin":175},"userChargedCurrency":{"free":50,"paid":10},"userMaterials":[{"materialId":1,"quantity":10}],"userPracticeTickets":[]},
         "response":{"boost":{"rewardRate":5},"scoreRankRewards":[{"resourceType":"coin","quantity":75},{"resourceType":"material","resourceId":1,"quantity":10}],
         "musicAchievementRewards":[],"playerRankRewards":[],"limitedTermScoreRankRewards":[]}}
        """)!.AsObject();
        var boxes = JsonNode.Parse("""
        [{"id":61,"resourceBoxPurpose":"score_rank_reward_detail","resourceBoxType":"expand","details":[{"resourceType":"coin","resourceQuantity":15}]},
         {"id":11,"resourceBoxPurpose":"score_rank_reward_detail","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":1,"resourceQuantity":2}]},
         {"id":12,"resourceBoxPurpose":"other","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":1,"resourceQuantity":2}]}]
        """)!.AsArray();
        var report = LiveRewardAudit.Analyze(record, boxes);
        check(report["balancesVerified"]!.GetValue<bool>() && report["balanceDifferences"]!.AsArray().Count == 0,
            "奖励审计核验实际增量，缺少旧持有行按零处理");
        check(report["scoreRewardCandidates"]![1]!["candidateBoxIds"]!.AsArray().Count == 1 &&
            report["scoreRewardCandidates"]![1]!["candidateBoxIds"]![0]!.GetValue<int>() == 11,
            "资源箱候选核对 purpose、资源、数量与倍率");
        record["after"]!["userMaterials"]![0]!["quantity"] = 9L;
        check(!LiveRewardAudit.Analyze(record, boxes)["balancesVerified"]!.GetValue<bool>(), "发放不足不能通过奖励审计");
        record["after"]!["userMaterials"]![0]!["quantity"] = 10L;
        record["after"]!["userPracticeTickets"]!.AsArray().Add(JsonNode.Parse("""{"practiceTicketId":1,"quantity":1}"""));
        check(!LiveRewardAudit.Analyze(record, boxes)["balancesVerified"]!.GetValue<bool>(), "响应外新增资源也必须报差异");
        record["response"]!["limitedTermScoreRankRewards"]!.AsArray().Add(JsonNode.Parse("""
        {"scoreRankRewardType":"fixture","obtainedRewards":[{"resourceType":"practice_ticket","resourceId":1,"quantity":1}]}
        """));
        check(LiveRewardAudit.Analyze(record, boxes)["balancesVerified"]!.GetValue<bool>(), "限时奖励通过 obtainedRewards 纳入增量核对");
        record["response"]!["musicAchievementRewards"]!.AsArray().Add(JsonNode.Parse("""{"resourceType":"jewel","quantity":20}"""));
        record["response"]!["playerRankRewards"]!.AsArray().Add(JsonNode.Parse("""{"resourceType":"jewel","quantity":50}"""));
        record["after"]!["userChargedCurrency"]!["free"] = 120L;
        check(LiveRewardAudit.Analyze(record, boxes)["balancesVerified"]!.GetValue<bool>(),
            "成就和升级水晶按实际数量合并核对，不额外乘评分掉落倍率");
        record["after"]!["userChargedCurrency"]!["free"] = 110L;
        record["after"]!["userChargedCurrency"]!["paid"] = 20L;
        check(LiveRewardAudit.Analyze(record, boxes)["balanceDifferences"]!.AsArray().Count == 2,
            "免费水晶误发到付费余额时分别报告差异，不能以总额相同通过");
        record["after"]!["userChargedCurrency"]!["free"] = 120L;
        record["after"]!["userChargedCurrency"]!["paid"] = 11L;
        check(!LiveRewardAudit.Analyze(record, boxes)["balancesVerified"]!.GetValue<bool>(),
            "响应外的付费余额变化也不能通过奖励审计");
        record["after"]!["userChargedCurrency"]!["paid"] = 10L;
        record["response"]!["playerRankRewards"]!.AsArray().Add(JsonNode.Parse("""{"resourceType":"stamp","resourceId":1,"quantity":1}"""));
        var unsupported = LiveRewardAudit.Analyze(record, boxes);
        check(!unsupported["balancesVerified"]!.GetValue<bool>() && unsupported["unsupportedResourceTypes"]!.AsArray().Count == 1,
            "未覆盖的资源类型明确保留未验证状态");
        record["before"]!.AsObject().Remove("userChargedCurrency");
        var missingBalance = false;
        try { LiveRewardAudit.Analyze(record, boxes); }
        catch (InvalidOperationException) { missingBalance = true; }
        check(missingBalance, "缺少水晶余额不能当作零余额通过审计");
    }
}
