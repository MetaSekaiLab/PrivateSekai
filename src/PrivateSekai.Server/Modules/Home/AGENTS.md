# 首页

- 负责区域动作、topic、appeal 与首页刷新；其他功能通过这里初始化商店动作。
- 固定商店动作来自既有实现，不能扩展为猜测的解锁规则。
- `information` 读取模板快照；主页刷新返回公共资源差异。
- 证据见 `docs/API.md` 对应路由；完整 Suite 和注册初始化由 `ProtocolChecks` 验证。
