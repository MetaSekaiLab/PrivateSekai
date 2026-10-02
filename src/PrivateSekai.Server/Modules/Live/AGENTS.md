# 演出

- 负责私有 Live 会话、成绩、音乐成就、boost 消耗和结算；任务进度通过 `MissionService` 更新。
- 所有结算步骤使用同一 UserSession，失败不得留下已删除会话或部分奖励。
- 开局使用协议要求的窄 `UpdatedResources`；结算使用完整刷新集。
- 经验、活动等占位结果保留原行为，补全前先查证客户端与 master。
- 证据见 `docs/API.md`；`FeatureChecks` 验证结算、失败回滚和重复提交。
