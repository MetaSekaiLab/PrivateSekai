# 账号

- 负责模板、新用户登记、认证和引继；账号私有字段为 `InheritId`、`InheritPassword`。
- 注册走 `Create`；仅验证凭证成功后允许 `Restore`。普通查询和业务操作不得隐式创建用户。
- `AccountTemplates` 保存模板字节，每次返回独立对象，禁止共享可变认证响应。
- 新用户需初始化 Home 的商店动作。凭证和引继信息不得写日志。
- 协议证据见 `docs/API.md` 的对应路由；模板、恢复和认证隔离由 `tools/ProtocolChecks` 验证。
