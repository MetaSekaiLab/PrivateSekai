# 名片

- 负责昵称、个人资料、自定义名片及资源使用数；资源 Handler 管理服装和 avatar motion。
- 缩略图写入独立存储，写入成功后更新用户引用；用户事务失败可能留下未引用图片。
- Handler 不回调名片 Service，不修改其他功能的货币或等级字段。
- 证据见 `docs/API.md`；实际响应和加解密路径由 `TransportChecks` 验证。
