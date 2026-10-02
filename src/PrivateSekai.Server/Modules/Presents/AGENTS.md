# 礼物

- 负责 `userPresents` 与私有 `PresentHistories`；领取通过资源服务发奖。
- 礼物移除、资源变化和领取历史必须属于同一次用户操作。
- 未知资源失败时保留原礼物，不吞掉发奖异常。
- 证据见 `docs/API.md`；用户私有状态隔离由 `UserOperationChecks` 验证。
