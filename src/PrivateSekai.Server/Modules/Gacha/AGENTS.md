# 抽卡

- 负责抽取、愿望、天井兑换及 `userGacha*` 状态；卡牌与服装通过资源服务发放。
- `GachaResourceHandler` 只管理票券和天井道具数量。
- 保留费用响应的现有剩余余额语义、奖励顺序和资源类型范围，不据字段名改协议。
- 首次获得卡牌的服装奖励由 Service 编排，Handler 不回调业务。
- 证据见 `docs/API.md`；`FeatureChecks` 包含确定性双抽及中途失败回滚。
