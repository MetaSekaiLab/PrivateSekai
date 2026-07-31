# AI 开发规则

本项目是 PrivateSekai 服务端。所有涉及客户端协议、用户状态、master data、资源路径、加密请求和响应结构的实现，都必须先确认证据来源，不得只凭 API 名称或字段名猜测业务。

可提交文档和代码不得包含本机绝对路径、用户名、真实账号、token、cookie、抓包原文或其他敏感信息。本地路径、工具位置、抓包样本位置、逆向仓库位置等只允许写入 `AGENTS.local.md` 或其他不提交到 Git 的 `*.local.md` 文件。

如果任务需要本地逆向资料、抓包或私有工具，但 `AGENTS.local.md` 不存在或信息不足，应直接向用户索取最小必要材料。

## 本地覆盖文件

仓库可放置一个不提交的 `AGENTS.local.md`，用于记录当前机器的私有路径和临时工作约定，例如：

- 客户端逆向代码位置。
- string literal、IDA 数据库、抓包样本、解密工具的位置。
- 本地运行端口、代理、证书、临时数据目录。
- 当前审计版本或只对本机有效的排查备注。

`AGENTS.local.md` 的规则只补充本文件，不应覆盖安全要求。涉及敏感信息时，以更严格的规则为准。

## 目录约定

- `src/PrivateSekai.Server/`: 服务端项目源码、项目文件和可运行配置模板。
- `docs/`: 可提交的项目文档，只写脱敏后的协议、设计和实现说明。
- `data/template/`: 模板用户和初始化 JSON。
- `data/suitemasterfile/`: suite master 数据包。已跟踪的 zip 和说明文件可以保留，展开目录和缓存不提交。
- `data/sekai-master-db-diff/`: 本地 master diff 或生成数据，不提交。
- `tools/`: 本地辅助脚本和分析工具。
- `tools/captures/`: 抓包和流量材料，不提交。

不要把临时脚本、抓包、原始 dump、反编译目录或生成缓存放回仓库根目录。

## 开发原则

- 先证据，后实现。路径、HTTP method、request/response model、MessagePack key、字段可空性和状态变更必须来自客户端审计、抓包、master data 或现有实现交叉验证。
- 不凭相似名称合并业务。相似 API、相似字段、相似 resource id 必须检查实际调用链或数据映射。
- 控制器只做路由、解密、参数校验和调用 service。复杂业务状态放在 `Services/` 或已有业务门面中。
- MessagePack model 放在 `Models/`，保持 `[MessagePackObject]` 和字符串 `[Key("...")]` 风格。
- 常规加密请求使用 `await ReadBodyAsync()` 后通过 `PrskCrypto.PrskDec<T>(body)` 解密；常规加密响应使用 `PrskResponse(responseData)`。
- 用户资源刷新优先返回 `SuiteUserCommonResponse { updatedResources = user.GetRefreshData() }`，除非已有证据说明该接口有更窄的返回集。
- 不用 `try-catch` 掩盖尚未理解的协议、模型或状态错误。只有 IO 边界、兼容降级或已有项目模式明确需要时才捕获异常。
- 不记录完整 cookie、token、authorization header、密码、手机号、邮箱或设备指纹。调试日志只记录是否存在、长度、哈希或脱敏摘要。

## 代码风格

- 保持实现直接、短小。没有明确复用、隔离或抽象价值时，不新增转发层、包装层或空 service。
- 优先沿用现有命名空间、controller 风格、service 风格和 model 风格。
- 使用 `?.`、`??` 等 C# 语法糖简化空值处理，但不要牺牲可读性。
- 删除未使用的 `using`。新增 `using` 按常规 C# 风格排序：System、第三方、项目命名空间。
- 不做与当前任务无关的格式化、批量重排或模型拆分。

## 文档规则

所有新增文档主体使用中文，专有名词、类名、API、命令、文件路径和代码可以保留英文。

文档应简洁、准确、中立。避免宣传性表述、过度包装和无法验证的结论。

项目文档中只使用相对路径。项目外路径、本机用户名、磁盘盘符、抓包文件绝对位置等必须写入不提交的 local 文档。

API 审计文档应说明：

- method 和 path。
- request 的 path/query/body 字段。
- response 字段和客户端实际用途。
- 客户端请求时机和后续处理。
- 证据不足时需要的最小补充材料。

## 验证要求

涉及 C# 代码、项目文件或运行配置的修改，完成后至少执行：

```powershell
dotnet build src\PrivateSekai.Server\PrivateSekai.Server.csproj
```

如果构建因正在运行的服务端进程、文件锁或本地环境问题失败，只记录失败原因和被锁文件，不主动停止用户进程，除非用户明确要求。

提交前检查：

```powershell
git status --short
rg -n "SekaiReverse|Assembly-CSharp|Desktop|Users|[A-Z]:[/\\]|token|cookie|authorization|password" docs src AGENTS.md
```

命中不一定都是问题，但必须人工确认没有真实敏感信息或本机路径进入可提交文件。
