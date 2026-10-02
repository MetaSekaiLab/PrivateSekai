# 卡牌

- 负责卡牌等级、特训、Master Rank、重复数和卡面；`userCards[].episodes` 的剧情状态由 Story 负责。
- `CardResourceHandler` 负责卡牌入库；Handler 不触发养成或任务流程。
- 养成通过资源服务扣发，任务联动调用 `MissionService`。
- master 查询集中在 `CardMasterQueries`；当前默认规则仍需按客户端证据核实。
- 证据见 `docs/API.md` 对应路由；验证见 `FeatureChecks`、`ResourceTests`。
