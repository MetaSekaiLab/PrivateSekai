# 模块开发

- 按需读取当前模块的文件和 `AGENTS.md`，不要一次加载全部 master 数据。
- 新功能放入所属目录，有实际职责才新增 Service、查询类或 Handler；在 `ServerServices.cs` 显式注册。
- Service 使用当前 scoped `UserSession`，只执行本功能规则。跨模块调用保持单向，依赖会由架构检查验证。
- 资源通过 `ResourceService` 扣发。新资源在所属模块实现 `IResourceHandler` 并注册；Handler 只改传入状态，不调用 Service、其他 Handler 或用户存储。
- 状态变化调用 `user.MarkChanged(nameof(SuiteUser.xxx))`，Service 不调用 `BuildRefresh` 或设置响应的 `updatedResources`。
- Controller 通过 `UserOperation` 包住完整业务和响应映射，用 `Encoded` 返回已编码结果。提交前异常自动丢弃副本。
- master 表和行按只读使用，查询放在所属模块；不往共享缓存加入业务规则。
- 为改动补充状态、刷新和失败路径检查；优先沿用 `tests/PrivateSekai.Tests` 的小型 fixture。
- 不根据重构后的类名补齐猜测规则。现有占位、资源白名单、默认值和未完成逻辑需要独立审计。
