# 剧情

- 负责剧情阅读、解锁及 `userCards[].episodes`，不修改卡牌等级和重复数。
- 资源收支通过共享资源服务，卡牌与任务规则使用对应查询。
- Controller 保留 `userBeginnerMissionBehavior` 的响应排除规则。
- 推荐接口仍有占位行为。
- 刷新排除与变化合并由 `UserOperationChecks` 验证。
