# API

**DO NOT SHARE!!**

**不要分享！！**

[MetaSekaiLab/PrivateSekai](https://github.com/MetaSekaiLab/PrivateSekai) by [MetaMiku](https://github.com/MetaMikuAI)

## GET `/api/suitemasterfile/{dataVersion}/{splitFile}`

- Path：版本及分片文件名取自认证响应的 `suiteMasterSplitPath`，不固定分片数量、哈希或表名。无 query、body。
- Response：加密 MessagePack map，键为 master 表名，值为行数组；一个分片包含多张表，可含空表。客户端在认证后据此加载当前版本数据。
- Client 操作：`suite-master-split`，参数为 `dataVersion` 和 `splitFile`。检查完整版本和文件名格式，拒绝路径穿越；错误对象不能作为 master 表接收。
- 官方静态缓存响应不返回轮换会话 token；读取后保留原会话。当前分片请求及紧接的 Suite 读取成功，确认该例外仅用于 master 静态读取，其他 API 保持原有轮换要求。
- 场景记录仍使用通用脱敏规则，适合核对数值和结构，不能直接当作完整、无损的 master 发布文件。

证据：认证返回的分片路径、已有分片下载与服务端实现、当前官方分片和后续 Suite 请求。隔离 HTTP 检查覆盖真实分片控制器的加密响应、多表及空表、非法路径、错误对象，以及无轮换 token 后继续业务请求。

## GET `/api/user/{userId}/diarkis-auth`

- Path：当前 `userId`；Query：`diarkisServerType`；无请求体。
- 类型来自 `MultiplayCore.ServerType`：`udp`、`multi`、`cheerful`、`virtual_live`、`streaming_live`、`rank_match`、`mysekai`、`custom_multi`。
- Response：`UserDiarkisAuthResponse`，含 `userId`、`clientKey`、TCP／UDP主机与端口、`sid`、`encryptionKey`、`encryptionIv`、`encryptionMacKey`。这些连接凭证不得写入可提交文件或日志。
- 时机：`MultiplayCore.Authentication` 在建立实时连接时请求，后续交给 `SetupProtocol`；该API回调只转交结果，不调用 `UpdateAll`，不生成Suite刷新。
- 证据：`GetUserDiarkisAuth.Execute`（RVA `0x6180754`）以method0、空body调用；`APICoreParam.Method`确认0为GET。对应导出字符串为 `user/{0}/diarkis-auth?diarkisServerType={1}`；编号按该dump的1基规则解析，不能直接用0基数组下标。

Client 已提供 `diarkis-auth` 操作，在场景 `args.diarkisServerType` 指定类型，不自动读写前后Suite。隔离HTTP检查验证了查询、无body、响应解密、凭证脱敏和后续会话轮换；服务器类型及参数注入另有检查。

官方 `multi` 样本已核验：HTTP200，返回 `userId`、`clientKey`、`udpHost`、`udpPort`、`sid` 和三项加密字段，未返回 `tcpHost`／`tcpPort`。响应有 `X-Session-Token`，随后独立Suite请求成功；场景记录和HTTP记录中的连接凭证均已脱敏。不能要求TCP与UDP同时存在：客户端 `IsValidTCP`／`IsValidUDP` 分别判断对应host是否非空，`SetupProtocol` 按可用分支配置连接。隔离HTTP夹具覆盖仅UDP字段的形状。其他服务器类型、错误语义、有效期及实时握手仍待核验；Server 未实现该接口，也不伪造连接凭证。

实时加密层已按 `Diarkis.Lib.Encryption` 的完整导出实现为 Client 的 `DiarkisEncryption`，尚未接入UDP收发。`SetupProtocol` 将SID及三项加密参数按十六进制解码。消息布局为4字节大端原文长度、32字节HMAC-SHA256、AES-CBC密文；MAC只覆盖密文，不覆盖长度头。加密前补1～16字节零，空原文和整块原文也额外补一块；解密按长度截取，保留原文末尾的零。AES模式及Padding枚举已与dump核对，不能复用HTTP的PKCS7封装。

验证包括独立Python加密库生成的空原文、16字节及17字节固定向量，认证码／密文损坏、截断和长度越界检查。非法长度本地拒绝，不声明为官方错误语义；未修改协议MAC覆盖范围。此结果证明编解码与静态规则及独立向量一致，尚无官方UDP报文对拍，不表示握手、可靠重传、业务命令或建房已完成。

Client 的 `DiarkisPacket` 已接入请求封装、响应解析和 UDP 头编解码，供后续实时连接使用。字段布局来自 `Packet.Create`（RVA `0x640e7f0`）、`ParseHeader`（`0x640ecf4`）、`Parse`（`0x640eb9c`）、`CreateUDPPacket`（`0x640e7a8`）和 `ParseUDPPacket`（`0x640ea84`）：

| 字段 | 请求偏移／长度 | 响应偏移／长度 | 编码 |
| --- | --- | --- | --- |
| 固定头标识 | 0／4 | 0／4 | `FE BE DE EF` |
| 版本 | 4／1 | 4／1 | 无符号字节 |
| 载荷长度 | 5／3 | 5／3 | 大端 24 位，不含业务头 |
| 命令 | 8／2 | 8／2 | 大端 16 位 |
| 状态 | 无 | 10／1 | 255 表示推送；其余状态交给业务解释 |
| 载荷 | 10 起 | 11 起 | 按声明长度读取 |

固定头标识来自 `HeaderSymbol_` 引用的原始 metadata 字段，并与字段名中的 SHA-256 一致；dump 将该字段显示为整数，其显示值不能代替原始初始化字节。`Create` 有反编译局部变量警告，布局另与独立解析逻辑及 `REQ_HEADER_SIZE=10`／`RES_HEADER_SIZE=11` 核对。

发送侧先加密业务载荷，再在加密封装前放 SID，最后添加业务头。接收侧按响应头取出载荷后直接解密，不剥离 SID；解析器返回消费长度，剩余数据由传输层继续处理。UDP 外层为 3 字节小端序号、1 字节标志及后续数据；标志 1 为普通 UDP，2～7 分别为 SYN、DAT、ACK、RST、EACK、FIN。底层解析保留未知标志，不将其解释为已支持的业务。

握手静态链路：`Connect_` 将初始发送序号设为 0；`SendSyn_`（`0x60b8258`）发送 UDP 头加 SID，不添加业务头或加密封装。收到 SYN 后 `HandleSyn_` 初始化连接并排队 ACK；收到 ACK／EACK 也有尚未握手时的初始化分支。随后 `SendClientKey_` 回调（`0x60bc170`）将 clientKey 按 UTF-8 编码，调用可靠发送，版本 0、命令 4。SID 和加密参数使用十六进制解码，不能把 clientKey 同样处理。

本地检查覆盖手工固定字节样本、全部长度字节、版本／命令边界、推送与其他状态、消费长度、SYN／ACK、UDP 标志与序号、截断／错误头及加密层组合。长度和序号越界在本地拒绝，不模仿原编码器的静默截断，也不声明为官方错误语义。尚未实现实时连接状态机、重传、分片和实际收发；没有新增官方 UDP 样本，Server 端认证和多人业务仍待实现。

## PUT `/api/user/{userId}/profile-honor`

- Path：当前 `userId`，无 query。Client 操作为 `profile-honor-save`。
- Body：`PutUserProfileHonorRequest.profileHonors`。每条包含 `seq`、`profileHonorType`、`honorId`、`honorLevel`、`bondsHonorViewType`、`bondsHonorWordId`，以及可空的 `honorBackgroundId`、`honorWordId`。
- Response：`SuiteUserCommonResponse.updatedResources.userProfileHonors`。成功后客户端通过 `UserDataManager.UpdateAll` 合并资源。
- 请求时机：称号设置页复制当前装备，修改目标槽位、移除空项后提交。普通称号使用 `profileHonorType=normal`、`bondsHonorViewType=none`、`bondsHonorWordId=0`；主、副槽位为 1～3。

普通称号官方样本确认：

- 装备列表全量替换，按 `seq` 升序返回。空列表清空装备，响应包含空数组；同值重复保存仍返回 200 和装备列表。
- 背景、文字可为空，此时响应省略对应字段。已持有等级为 1 时，请求等级 0、1、2 均保存并返回等级 1；服务端使用持有等级。
- 未持有称号、未持有的同组背景或文字、已持有但属于其他组的背景或文字，均返回 409，错误体为 `httpStatus` 与空字符串 `errorCode`、`errorMessage`。独立登录回读确认这些拒绝不修改称号相关状态。
- 背景和文字通过 master 的 `honorGroupId` 对应称号 `groupId`，不按 ID 前缀推断。

证据：`PutUserProfileHonorAPI`、`ScreenLayerHonorSetting.ExecuteHonorAPI`、请求响应与称号模型、master 映射和官方请求记录。7 份成功、6 份拒绝样本的专项 HTTP 对拍通过，覆盖装备、持有称号、背景、文字、相关响应及独立回读；不代表完整 Suite 已对齐。

当前实现普通称号。羁绊称号、重复称号、非法或重复槽位、更高持有等级及其他称号类别仍待样本；本地对超出支持范围的输入校验不声明为官方错误语义。

三槽保存会推进 `set_profile_honors_full` 新手任务。官方对照确认：两槽不创建该任务，首次填满三槽进度为1且本次 `isNewAchieved=true`；同值保存继续增至2，领奖后再次三槽保存增至3并保留 `received`。减少到两槽或清空不增加进度；Suite回读的临时提示为false。未新达成时不额外刷新任务状态。

Server 按 master 类型查询全部对应任务及门槛，成功保存三个有效槽位后调用通用累计逻辑，失败和编码异常整体回滚；不绑定任务12或门槛1。`--replay-profile-honor-mission <record> <master> <output>` 通过独立客户端读取一次写后Suite，核验穿戴、持有称号、新手进度、任务状态及临时提示。六份官方对照全部一致；另用非官方任务编号和多门槛夹具验证泛化，不将羁绊称号等未接入类别计入验证范围。

## PUT / PATCH `/api/user/{userId}/myList/{listNo}`

- Path：当前 `userId`、列表号 `listNo`。客户端提供 1～5 号列表；无 query。
- PUT body：`PutUserMusicMyListRequest`，包含名称 `name` 和歌曲数组 `musicIds`。Client 操作为 `music-my-list-save`。
- PATCH 不发送 body，清空指定列表的歌曲。Client 操作为 `music-my-list-reset`。
- Response：分别使用 `PutUserMusicMyListResponse`、`PatchUserMusicMyListResponse`；资源键为 **`updateResources`**，其中 `userMyLists` 包含 `listNo`、`name`、`musicIds`。C# 字段名分别为 `updatedResources` 和 `userMusicMyList`，以 dump 序列化契约为准。
- 请求时机：歌单编辑与改名使用 PUT；重置确认后使用 PATCH。成功回调通过 `UserDataManager.UpdateAll` 合并资源，刷新歌曲、数量与重置按钮。客户端改名流程拒绝空名称和禁用词，发送前去除换行。

官方样本确认：PUT 按歌曲 ID 升序保存，保留重复项，允许未持有歌曲但不解锁歌曲；更新单个列表保留其他列表，响应返回全部已有列表。名称和排序后的歌曲均未变化时返回 400。空歌曲列表可以改名。

PATCH 保留名称和列表记录，只清空歌曲；重复重置空列表返回 404。上述 400、404 的错误体为 `httpStatus` 与空字符串 `errorCode`、`errorMessage`。Server 在同一用户操作中修改状态，编码失败回滚。

证据：两项 API 的 Execute 与成功回调、请求响应模型、`MusicUtility` 和 `MusicMyListLayer`，以及官方保存、改名、多列表、清空和拒绝样本。12 份成功记录的列表、音乐持有状态及响应专项 HTTP 重放无差异；两份拒绝记录的状态码、错误体及基线一致，本地独立会话回读不变，官方后续登录回读也未改变列表。名称另以测试场景和在线响应断言核对。

非法列表号、名称长度和禁用词的服务端错误格式、歌曲数量上限及无效歌曲 ID 尚未采样；现有检查不代表完整输入边界或全部 Suite 字段已对齐。

## GET `/api/system`

> 审计版本: jp-6.5.5
> 关键词：登录，维护，版本，服务器

获取当前系统状态信息。客户端用它确认服务器时间、维护状态、当前版本可用性，以及对应的资源版本和多人版本。

### 请求参数

无参数。

### 返回字段

- `serverDate`: 服务器当前时间，毫秒时间戳。
- `timezone`: 服务器时区。
- `profile`: 系统环境标识，例如 `production`。
- `maintenanceStatus`: 维护状态，例如 `maintenance_out`。
- `appVersions`: 客户端版本列表，包含 `appVersion`、`assetVersion`、`multiPlayVersion`、`appVersionStatus` 等版本状态信息。

### 客户端请求时机

客户端不只在登录时请求这个接口，目前确认有这些时机：

1. 标题页登录流程中，master 数据加载成功后请求。
   - 请求成功后客户端会保存登录日期/服务器时间。
   - 随后继续请求完整用户数据。

2. 进入 OutGame 场景启动流程时请求。
   - 场景启动时会先拉取系统信息。
   - 回调后根据启动参数决定是否下载 start app 资源列表，或者继续后续 OutGame 初始化。

3. Streaming / RealTimeLive 的服务器时间同步时请求。
   - 实时 live 计时器初始化时会请求 `api/system`。
   - 主要使用返回的 `serverDate` 校准本地计时，避免实时演出/回放时间和服务器时间偏移。

### 客户端切入点

- `Sekai.GetSystemAPI.Execute`: 确认请求为 `GET system`，请求模型为 `EmptyRequest`，响应模型为 `SystemResponse`。
- `Sekai.GetSystemAPI.OnCallBack`: 确认成功后会保存系统数据。
- `Sekai.TitleController.OnFinishLoadMaster`: 标题页 master 加载完成后发起系统信息请求。
- `Sekai.TitleController.OnFinishSystemAPI`: 系统信息成功后保存登录日期、重置用户数据，并继续请求完整用户数据。
- `Sekai.OutGameController.ExecuteSystemAPI` / `OnFinishOutGameStartSystemAPI`: OutGame 启动阶段的系统信息请求入口。
- `Sekai.NewsUtility.SyncTimeAsyncInternal` / `Sekai.Streaming.Api.SendWebRequest`: 可用于复现服务器时间同步相关请求。

## PUT `/api/user/{userId}/auth`

> 审计版本: jp-6.5.5
> 关键词：登录，认证，session，规约，用户数据

认证已有账号并刷新客户端会话。客户端用本地保存的凭证和设备信息换取新的 `sessionToken`，同时接收版本信息、资源差异、规约状态、封禁信息和后续 master 加载所需路径。

### 请求参数

- Path `userId`: 用户 ID。
- Query `refreshUpdatedResources`: 是否要求返回并应用 `updatedResources`。普通执行路径会带这个参数；首个串行认证路径可以不带 query。
- Query `isIgnoreRuleAgreement`: 是否忽略规约同意阻断。仅特定标题菜单流程会带 `true`。
- Body `credential`: 用户凭证，必填。
- Body `deviceId`: 设备 ID。客户端只有在本地值非空时才发送。
- Body `authTriggerType`: 认证触发原因。普通认证为 `normal`，连接错误后重试认证为 `connection_error`；忽略规约同意的请求不发送该字段。

### 返回字段

- `sessionToken`: 后续 API 使用的新会话 token。
- `appVersion`: 当前客户端版本。
- `removeAssetVersion`: 登录后需要清理的旧资源版本。
- `dataVersion`: master 数据版本。
- `assetVersion`: 资源版本。
- `multiPlayVersion`: 多人玩法版本。
- `assetHash`: 资源 hash。
- `appVersionStatus`: 当前版本状态，例如 `available`。
- `updatedResources`: 需要合并进本地用户数据的资源差异；当 `refreshUpdatedResources=true` 时客户端会重点使用。
- `isStreamingVirtualLiveForceOpenUser`: 是否强制开放 Streaming Virtual Live 入口。
- `deviceId`: 服务端确认或更新后的设备 ID，客户端会写回本地。
- `userBanInfo`: 用户封禁信息；存在时标题页会展示封禁提示并停止正常登录后续流程。
- `suiteMasterSplitPath`: 分片 master 数据路径，标题页后续加载 master 时使用。
- `obtainedBondsRewardIds`: 已获得的羁绊奖励 ID 列表；非空时客户端会记录到本地用户数据相关状态。

### 客户端请求时机

客户端不只在标题页登录时请求这个接口，目前确认有这些时机：

1. 标题页正常登录流程中请求。
   - 客户端先获取签名 cookie，再请求 app info。
   - 如果本地已有账号，直接请求 auth；如果没有账号，会先注册用户，注册成功后再请求 auth。
   - 标题页这次 auth 请求中，`refreshUpdatedResources=false`，`authTriggerType=normal`。
   - 请求成功且没有封禁、规约阻断时，客户端保存版本信息和 `sessionToken`，然后用 `dataVersion`、本地 snapshot 和 `suiteMasterSplitPath` 加载 master。
   - master 加载成功后继续请求系统信息，再请求完整用户数据。

2. auth 返回需要同意规约时触发规约流程。
   - 客户端会先拉取待同意规约列表，并逐项弹出同意弹窗。
   - 用户确认后客户端提交规约同意结果。
   - 提交完成后会立刻重新请求普通 auth，重新进入标题页登录后续流程。
   - 同一段流程里还会注册一个约 1.5 秒后的等待提示回调；这不是重新 auth 前的等待。

3. 普通 API 执行前发现本地已登录但 `sessionToken` 为空时请求。
   - 客户端会先补一次认证。
   - 补认证这次 auth 请求中，`refreshUpdatedResources=true`，`authTriggerType=normal`。
   - 认证成功后会更新 session、版本信息、设备 ID 和用户数据差异，再继续原本 API 执行队列。

4. API 因 token 无效进入重试流程时请求。
   - 客户端会先重新认证；认证成功后，复用刚才因为 token 无效失败的请求对象，再调用一次同一个 API。
   - 重试认证这次 auth 请求中，`receiveUserData=true`，`authTriggerType=connection_error`，并走首个串行认证路径；请求 path 不带 `refreshUpdatedResources` query。

5. 标题菜单中的用户删除确认流程会请求忽略规约同意的 auth。
   - 用户在标题菜单确认删除用户后，请求会带 `isIgnoreRuleAgreement=true`，`refreshUpdatedResources=false`。
   - 成功或仍需要规约确认时，客户端都会继续拉取用户游戏数据。
   - 用户数据拉取完成后展示用户删除确认对话框。

### 客户端切入点

- `Sekai.PutUserAuthAPI.PutUserAuthAPI`: 确认 request body 字段来源：`credential`、可选 `deviceId`、`authTriggerType`、`receiveUserData`。
- `Sekai.PutUserAuthAPI.Execute`: 确认普通请求 path 为 `user/{userId}/auth?refreshUpdatedResources={bool}`，method 为 PUT。
- `Sekai.PutUserAuthAPI.ExecuteFirst`: 确认首个串行认证 path 为 `user/{userId}/auth`，不带 `refreshUpdatedResources` query。
- `Sekai.PutUserAuthAPI.OnCallBack`: 确认成功后写入 session token、合并 `updatedResources`、保存版本信息、更新 deviceId 和羁绊奖励 ID。
- `Sekai.PutUserAuthIgnoreRuleAgreementAPI.Execute`: 确认忽略规约路径追加 `isIgnoreRuleAgreement=true`，且 request body 不发送 `authTriggerType`。
- `Sekai.UserAccountManager.Authentication`: 标题页普通认证、补 session、连接错误后重试认证的统一入口。
- `Sekai.UserAccountManager.AuthenticationIgnoreRuleAgreement`: 标题菜单用户删除确认流程的忽略规约认证入口。
- `Sekai.TitleController.Authentication` / `OnFinishAuthentication`: 标题页认证发起和认证结果后续处理入口。

## POST `/api/user`

> 审计版本: jp-6.5.5
> 关键词：登录，注册，新用户，凭证

注册新用户账号。客户端在本地没有已保存账号时调用它，拿到新用户登记信息、后续认证用凭证，以及一份初始用户数据。

### 请求参数

- Body `platform`: 平台字符串；Android 客户端固定发送 `Android`。
- Body `deviceModel`: 设备型号，来自客户端设备信息。
- Body `operatingSystem`: 操作系统版本，来自客户端设备信息。

### 返回字段

- `userRegistration`: 新账号登记信息；客户端用它创建本地账号记录。
- `credential`: 新账号凭证；客户端会和 `userRegistration` 一起保存，后续认证时使用。
- `updatedResources`: 初始用户数据。客户端在接口成功后会先合并这份数据，再执行外层注册完成处理。

### 客户端请求时机

目前确认有这些时机：

1. 标题页登录流程中，本地没有已保存账号时请求。
   - 客户端先获取签名 cookie，再请求 app info。
   - app info 成功后会加载本地账号、清理旧登录状态；如果本地账号不存在，就显示注册等待提示并请求 `POST /api/user`。
   - 请求成功后客户端合并 `updatedResources`，用 `userRegistration` 和 `credential` 创建本地账号。
   - 本地账号创建成功后继续普通认证流程；如果广告 SDK 尚未初始化，会先初始化 SDK，再进入认证。

### 客户端切入点

- `Sekai.PostUserAPI.Execute`: 确认请求为 `POST user`，request 为 `UserAPIRequest`，response 为 `UserAPIResponse`。
- `Sekai.PostUserAPI.Execute`: 确认 request body 填入 `platform`、`deviceModel`、`operatingSystem`。
- `Sekai.PostUserAPI.OnCallBack`: 确认成功后会先合并 `response.updatedResources`。
- `Sekai.UserAccountManager.RegisterUser`: 注册 API 的执行入口。
- `Sekai.UserAccountManager.OnFinishedPostUserAPI`: 确认用 `userRegistration` 和 `credential` 创建本地账号。
- `Sekai.TitleController.OnFinishAppInfoAPI` / `OnFinishRegisterUser`: 标题页判断无本地账号后注册，并在注册成功后继续认证。

## GET `/api/suite/user/{userId}`

> 审计版本: jp-6.5.5
> 关键词：登录，用户数据，全量，刷新

拉取指定用户的完整 `SuiteUser` 数据。客户端用它在登录后建立完整本地用户状态，也会在部分功能检测到本地状态可能过期时用它做全量同步。

### 请求参数

- Path `userId`: 当前用户 ID。
- Query `isLogin`: 登录后首轮拉取完整用户数据时会带 `true`；普通全量同步路径不带该参数。
- Body: 无请求体。

### 返回字段

- `SuiteUser`: 完整用户数据对象；客户端成功收到后会整体合并到本地用户数据管理器。
- `now`: 当前服务器时间，包含在 `SuiteUser` 中。
- `userRegistration`、`userGamedata`、`userTutorial`、`userConfig`: 登录后初始化用户基础状态时会使用。
- 其他 `user...` 字段: 用户持有资源、功能状态、任务/活动/商店/虚拟 Live/MySekai 等模块数据；客户端按本地数据管理器规则更新对应模块。

### 当前官方读取副作用

7.0.0 官方对照中，两个各有2位好友的测试账号连续请求完整 Suite，`make_new_friend` 新手任务进度每次增加2；中间插入 `user_friend` 分片请求，下一次完整 Suite 的增量仍为2。零好友账号的同序列没有生成该任务记录。随后移除两测试账号之间的一条关系，单好友账号的连续完整 Suite 进度为751、752、753、754，插入分片仍不增加额外进度。HTTP记录确认每步只有一次请求，客户端禁用自动重定向；请求均未带 `isLogin`。客户端 `GetSuiteUserAPI.OnCallBack` 只合并响应，协议采样程序不修改任务进度。

Server 根据0／1／2好友对照，在完整 Suite 和常规 `updatedResources` 构造时，按当前 `friendStatus=friend` 的数量累加 master 类型为 `make_new_friend` 的任务；指定好友分片读取不调用此更新，零好友不创建记录。挑战开局、结算、特训的官方响应已含本次增量，后续Suite再累加一次；删除好友的响应按删除后的好友数增加。共用刷新入口在每次用户操作内只执行一次，即使多次构造响应也不重复计数；编码失败随业务回滚，双账号操作只刷新调用方。业务检查覆盖多门槛、已领取后累积和回滚；真实HTTP检查覆盖完整Suite、业务响应、分片及好友数变化。更多好友数按同一规则泛化，未逐一官方采样；待处理申请和已领取状态的组合尚无独立官方对照。

完整 Suite 不能作为绝对无副作用的业务快照。写前、写后快照之间的该任务变化不能全部归因于被测业务。挑战阶段回放现分别比较业务响应中的任务及后续独立Suite的任务状态，导入好友关系状态以重现更新；不能直接忽略该字段后宣布完整对齐。其他专项回放仍需按各自实际读取顺序核验。

### 客户端请求时机

客户端不只在标题页登录时请求这个接口，目前确认有这些时机：

1. 标题页登录流程中，系统信息请求成功后请求。
   - 客户端在认证成功后加载 master 数据。
   - master 数据加载成功后请求系统信息；系统信息成功后保存登录日期、重置本地用户数据，并请求完整 `SuiteUser`。
   - 这次请求会带 `isLogin=true`。
   - 请求成功后客户端合并完整用户数据，初始化购买模块，关闭等待提示，初始化 tutorial，刷新未读 topic，并标记登录完成。

2. Live 结果或多人相关流程遇到结果已结束、状态冲突类错误时请求。
   - 客户端会请求完整用户数据来重新同步本地状态。
   - 同步完成后再继续对应的结果页退出、错误处理或界面恢复流程。

3. 部分功能页发现本地用户数据不足或可能需要更新时请求。
   - 例如活动故事列表为空时，会先全量同步用户数据，再重新构建故事列表。
   - Gacha 兑换相关页面检测到需要更新用户状态时，也会请求完整用户数据并等待同步完成。

### 客户端切入点

- `Sekai.GetSuiteUserAPI.GetSuiteUserAPI`: 构造参数 `isLogin` 决定是否追加登录 query。
- `Sekai.GetSuiteUserAPI.Execute`: 确认基础 path 为 `suite/user/{userId}`；登录路径追加 `?isLogin=true`；request 为 `EmptyRequest`，response 为 `SuiteUser`。
- `Sekai.GetSuiteUserAPI.OnCallBack`: 确认成功后整体合并返回的 `SuiteUser`。
- `Sekai.TitleController.OnFinishSystemAPI` / `OnFinishSuiteUser` / `PostProcessLogin`: 登录后完整用户数据拉取和后处理入口。
- `Sekai.ScreenLayerLiveResultBase.LiveResultAlreadyEndErrorHandler`: Live 结果状态过期后的全量同步入口之一。
- `Sekai.RankLive.Result.ScreenLayerRankLiveResult.LiveFinishAPIErrorHandler`: Rank Live 结果冲突后的全量同步入口。
- `Sekai.CheerfulCarnival.MatchingRoom.LiveFinishAlreadyEndErrorHandler`: Cheerful Carnival 结果冲突后的全量同步入口。
- `Sekai.ScreenLayerEventStorySelect.CreateStoryList`: 活动故事列表缺少数据时的全量同步入口。
- `Sekai.ScreenLayerGachaItemExchange.CheckUpdateSuiteUser`: Gacha 兑换页检测用户状态更新的入口。

## GET `/api/suite/user/{userId}/parts`

> 审计版本: jp-6.5.5
> 关键词：用户数据，局部刷新，好友，MySekai

按 `name` 拉取指定用户数据片段。客户端当前确认用它刷新好友相关数据，返回仍是 `SuiteUser` 结构，但通常只需要包含请求的片段。

### 请求参数

- Path `userId`: 当前用户 ID。
- Query `name`: 要刷新的数据片段名；目前确认客户端发送 `user_friend`。
- Body: 无请求体。

### 返回字段

- `SuiteUser`: 局部用户数据对象；客户端成功收到后会合并到本地用户数据管理器。
- `userFriends`: 当 `name=user_friend` 时客户端关注的好友数据。

### 客户端请求时机

目前确认有这些时机：

1. MySekai 访问列表设置或刷新好友数据时请求。
   - 客户端取当前用户 ID，请求 `name=user_friend` 的用户数据片段。
   - 请求成功后合并返回数据，再继续访问列表的好友相关显示或回调处理。

2. MySekai 住宅比赛页面获取好友数据时请求。
   - 页面模型会异步请求 `name=user_friend`。
   - 客户端等待请求完成后继续后续页面状态处理。

### 客户端切入点

- `Sekai.Api.GetSuiteUserPartsApi.GetSuiteUserPartsApi`: 构造参数为目标 `userId`。
- `Sekai.Api.GetSuiteUserPartsApi.Execute`: 确认 path 为 `suite/user/{userId}/parts?name=user_friend`，method 为 GET，response 为 `SuiteUser`。
- `Sekai.Api.GetSuiteUserPartsApi.OnCallBack`: 确认成功后合并局部 `SuiteUser`。
- `Sekai.Mysekai.MysekaiVisitListDialog.Setup` / `RefreshFriend`: MySekai 访问列表刷新好友数据入口。
- `Sekai.Mysekai.MysekaiHousingCompetition.ScreenLayerMysekaiHousingCompetitionModel.GetUserFriend`: MySekai 住宅比赛页获取好友数据入口。

## PUT `/api/user/{userId}/profile`

> 审计版本: jp-6.5.5
> 关键词：个人资料，留言，Twitter，头像，用户资源

更新玩家个人资料中的留言、Twitter ID 和头像显示信息。客户端在个人资料页离开或保存时检测到资料字段变化后提交，成功后合并返回的用户资源差异。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `userId`: 请求体中的用户 ID 字段；客户端模型保留该字段。
- Body `word`: 个人资料留言；客户端会把空值归一为空字符串。
- Body `honorId1`、`honorId2`、`honorId3`: 个人资料称号 ID；该字段存在于请求模型中，但普通资料保存流程不负责称号更新。
- Body `twitterId`: Twitter ID；客户端会把空值归一为空字符串。
- Body `profileImageType`: 枚举为 `leader`、`card_before_special_training`、`card_after_special_training`，不是 `card`。普通资料保存流程会发送 `leader`。
- Body `profileImageId`: 资料头像 ID，可为空。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据，主要用于刷新 `userProfile` 相关状态。

官方样本已确认持有卡牌的 `card_before_special_training` 保存、特训完成卡牌的 `card_after_special_training` 保存，以及 `leader` 且省略 `profileImageId` 的恢复。响应和回读的 `userProfile` 保留当前 `userId`；恢复队长头像后省略 `profileImageId`。头像选择与好友资料中的主队队长卡分开，三个头像类型均已验证这一映射。

头像参数边界如下；除非法类型外，错误体均为 `ClientErrorResponse`，包含对应 `httpStatus`、空 `errorCode` 与空 `errorMessage`。

| 请求组合 | 官方结果 |
| --- | --- |
| 非枚举类型 `card` | 400，零字节正文 |
| `leader` 附带 ID（已采样 0 和已持有卡牌） | 400 |
| 特训前自选头像缺少 ID | 400 |
| 特训前自选头像 ID 为 0 或未持有的有效卡牌 | 404 |
| 不可特训、状态为 not_doing 的卡牌选特训后头像 | 409 |
| 特训完成的卡牌选特训后头像 | 200 |

Client 的 `profile-save` 保留可空 ID，不把缺失值补成 0。Server 在事务中校验头像与卡牌状态，拒绝时不更新资料；特训后头像按 `specialTrainingStatus=done` 放行。新补 8 组 HTTP 对拍覆盖表中拒绝、特训后成功与恢复，比较错误体或资料响应、本人独立回读及另一账号好友列表，均无差异。先前两组特训前头像与恢复对拍仍保留；编码失败回滚另有业务检查。未特训但可特训的高稀有度卡牌、生日卡、空字符串／缺失类型及组合错误优先级仍需样本，不能视为整个个人资料接口已完全核验。

留言另已确认：`profile_word_max_length` 为 30，30 个日文字符保存成功，31 个返回 400、空错误码。master 已列出的 NG 留言返回 409、`contain_ng_word`；两个拒绝均保留原资料。Server 使用 master 上限与 NG 词表，在资料写入之前校验。省略 `word` 和 `twitterId` 会清除已保存文本，并在响应与回读中省略这两个字段，因此本接口不能按只更新非 null 字段实现。

6 组文本 HTTP 对拍覆盖非空保存、省略清除、长度边界、超长拒绝、NG 拒绝与恢复。样本中文本已脱敏，回放从私有场景补入原请求，比较字段存在性及脱敏后的响应／回读，不能据此证明文本原样或字符归一化规则。原文赋值由本地检查覆盖；后续组合错误和空值样本见下文。

后续组合样本确认：头像校验优先于留言校验，留言长度优先于 NG 词。leader 附带 ID 并含 NG 词返回 400；未持有卡牌并有超长留言返回 404；未特训卡选特训后头像并含 NG 词返回空错误码的 409；超长且含 NG 词返回 400。NG 词嵌入普通留言仍返回 `contain_ng_word`。显式空字符串保存后保留 word／twitterId 字段，显式 null 则省略，均返回 200。服务端已按此顺序修正，另 8 组 HTTP 对拍通过。

补充平面字符仍有证据缺口：30 个同一表情返回 400，15 个返回 500、空正文，独立回读资料未变。仅前者纳入错误响应对拍；后者没有作为稳定业务规则实现，也不能据此确认字符计数方式。社交 ID 格式、NG 大小写与其他组合错误仍待核验。

### 客户端请求时机

目前确认有这些时机：

1. 玩家个人资料页离开或保存时请求。
   - 客户端记录进入页面时的留言和 Twitter ID。
   - 离开/保存时若这两个字段发生变化，会构造资料更新请求。
   - 请求成功后 API 层合并 `updatedResources`，页面侧完成后续关闭或刷新流程。

2. 称号更新不走这个普通资料保存流程。
   - 请求模型中虽然有 `honorId1`、`honorId2`、`honorId3`。
   - 客户端另有独立的 profile honor API 处理称号保存，不能只根据字段名把称号更新归到本接口。

### 客户端切入点

- `Sekai.PutUserProfileAPI.Execute`: 确认 path 为 `user/{userId}/profile`，method 为 PUT，request 为 `PutUserProfileRequest`，response 为 `SuiteUserCommonResponse`。
- `Sekai.PutUserProfileAPI.OnCallBack`: 确认成功后合并 `response.updatedResources`。
- `Sekai.PutUserProfileRequest`: 确认请求字段为 `userId`、`word`、`honorId1`、`honorId2`、`honorId3`、`twitterId`、`profileImageType`、`profileImageId`。
- `Sekai.ScreenLayerPlayerProfile.UpdateProfielAPI`: 个人资料页提交留言和 Twitter ID 修改的入口，并确认普通流程会把空字符串归一化。
- `Sekai.ScreenLayerPlayerProfile.OnFinishUserProfileAPI`: 个人资料更新请求完成后的页面回调入口。

## PATCH `/api/user/{userId}`

> 审计版本: jp-6.5.5
> 关键词：玩家名，昵称，userGamedata，用户资源

更新玩家名。客户端把新的玩家名放在 `userGamedata.name` 中提交，成功后合并用户资源差异；该路径是裸 `user/{userId}`，客户端行为确认的 method 为 PATCH。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `userGamedata.name`: 新玩家名。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据，主要用于刷新 `userGamedata.name`。

### 客户端请求时机

目前确认有这些时机：

1. 玩家名输入对话框确认时请求。
   - 客户端从输入框取玩家名；输入为空时会回退到占位文本。
   - 请求成功后合并 `updatedResources`，再执行对话框完成流程。

2. 自定义档案的玩家名编辑内容结束编辑时也会请求。
   - 编辑控件在结束输入后构造同一请求模型。
   - 客户端等待请求完成后再更新对应编辑视图状态。

### 客户端切入点

- `Sekai.PatchUserNameAPI.Execute`: 确认 path 为 `user/{userId}`，method 为 PATCH，request 为 `UserNameAPIRequest`，response 为 `UserNameAPIResponse`。
- `Sekai.PatchUserNameAPI.OnCallBack`: 确认成功后合并 `response.updatedResources`。
- `Sekai.UserNameAPIRequest`: 确认请求体只有 `userGamedata`。
- `Sekai.UserNameGameData`: 确认 `userGamedata` 内的字段为 `name`。
- `Sekai.InputNameDialog.ExecuteAPI` / `OnFinishUserNameAPI`: 玩家名输入对话框的提交和完成入口。
- `Sekai.CustomProfile.EditUserNameContentView.OnEndEditNameAsync`: 自定义档案编辑玩家名时复用同一 API 的入口。

## POST `/api/user/{userId}/shop/{shopId}/item/{shopItemId}`

> 审计版本: jp-6.5.5
> 关键词：商店，购买，歌曲，贴图，Another Vocal

购买商店项目。客户端把 `shopId` 和 `shopItemId` 拼入 path，不发送请求体；成功后合并返回的用户资源差异，并由对应购买弹窗继续关闭、刷新或展示购买结果。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `shopId`: 商店 ID。
- Path `shopItemId`: 商店项目 ID。
- Body: 无参数。客户端内部的 `UserShopRequest` 只用于保存 `shopId` / `shopItemId` 并拼 path。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据，常见影响包括 `userShops`、消耗资源以及购买所得资源。

### 客户端请求时机

目前确认有这些时机：

1. 音乐商店购买歌曲时请求。
   - 玩家在歌曲商店详情弹窗确认购买后，客户端用当前商品的 `shopId` 和 `shopItemId` 执行请求。
   - 请求成功后 API 层合并 `updatedResources`，详情弹窗进入完成/关闭流程。

2. 贴图商店购买 stamp 时请求。
   - 玩家在贴图商品详情弹窗确认购买后请求。
   - 请求成功后合并用户资源并关闭购买弹窗。

3. Another Vocal 购买确认时请求。
   - 玩家在 Another Vocal 购买确认弹窗点击确认后请求。
   - 请求成功后合并用户资源并关闭确认弹窗。

4. 区域商店首次购买项目时请求。
   - 区域商店详情弹窗在购买分支使用 POST。
   - 成功后除了合并 `updatedResources`，还会刷新区域侧展示数据并关闭弹窗。

### 客户端切入点

- `Sekai.PostUserShopAPI.Execute`: 确认 path 为 `user/{userId}/shop/{shopId}/item/{shopItemId}`，method 为 POST，request body 为空，response 为 `UserShopResponse`。
- `Sekai.PostUserShopAPI.OnCallBack`: 确认成功后合并 `response.updatedResources`。
- `Sekai.UserShopRequest`: 确认 `shopId` 和 `shopItemId` 只作为 path 参数来源。
- `Sekai.MusicShopDetailDialog.OnClickOK` / `OnFinishedPostUserShopAPI`: 音乐商店购买入口和完成回调。
- `Sekai.StampShopDetailDialog.OnClickOK` / `OnFinishedPostUserShopAPI`: 贴图商店购买入口和完成回调。
- `Sekai.AnotherVocalPurchaseConfirmDialog.OnClickOK` / `OnFinishedPostUserShopAPI`: Another Vocal 购买入口和完成回调。
- `Sekai.AreaShopDetailDialog.OnClickOK` / `OnFinishedPostUserShopAPI`: 区域商店购买分支入口和完成回调。

### 贴图购买实现与核验

- 普通单角色 illustration 贴图：按 `shopItems` 的材料成本扣券，通过 `resourceBoxes` 发放贴图，持有记录保存 `stampId` 和 `obtainedAt`，省略 `userId`。重复授予保留原取得时间。
- 官方首次购买样本同步推进该角色的 `collect_stamp` 任务与同名荣誉进度；角色任务当次达成项仅放入响应，不持久化提示。
- 商品状态持久化为 `sold_out`，购买响应省略 `userShops`；后续 Suite 回读显示售罄。材料、贴图、商店及相关任务的专项 HTTP 重放无差异，不覆盖其他背景字段。
- 已售罄商品重复购买、缺少兑换券均采得官方 HTTP 409，Server 已对应拒绝且不改变库存和任务。使用 `ClientErrorResponse`：缺券为 `not_enough_resource`，重复购买为空错误码，`errorMessage` 为空；两类错误响应与独立会话回读已完成 HTTP 对拍。
- 多角色贴图、其他成本及收集称号达成领奖的官方样本仍待补充；称号门槛现按下述通用规则处理。

Client 复用 `shop-purchase`，专项重放使用 `--replay-stamp-shop`。证据为贴图商店调用链、贴图与商店 master、首次购买及拒绝样本。

### Another Vocal 购买实现与核验

- 请求复用本节 POST 路由，无 query 和 body。成本来自 `shopItems.costs`，所选音源来自 `resourceBoxes` 与 `musicVocals`；已有兑换券按实际成本扣除，不把角色编号当作材料编号。
- 单角色与双角色 `another_vocal` 官方样本均新增 `userMusicVocals`（`musicId`、`musicVocalId`），商品持久化为 `sold_out`，响应刷新完整 `userShops`。基础歌曲未持有时仍可购买音源，不隐式解锁歌曲。
- 每个参与角色的 `collect_another_vocal` 进度各加 1，荣誉同名进度按购买的一个音源加 1；双角色版本不把荣誉加 2。角色任务的当次达成项只出现在响应，Suite 回读为空提示列表。
- 缺券与重复购买均返回 HTTP 409；分别使用 `not_enough_resource` 和空错误码，错误消息为空。拒绝后库存、音源和相关任务不变。
- 单角色、双角色购买及两种拒绝的专项 HTTP 对拍无差异，范围为音源、歌曲、商店、材料及相关任务。新手任务未变化；其他背景字段不在核验范围内。
- 当前接入 `characters` 为 `game_character` 的版本和材料成本。其他音源类型、角色类型仍未扩展；收集称号跨门槛不再阻止购买，规则及证据边界见下文。

收集称号门槛：贴图、Another Vocal 和服装制作共用 `honorMissions` 的类型与 requirement，不写死 30、10、50 等门槛。每次成功购买一个音源或制作一套服装仍只加一次荣誉进度；双角色音源分别增加角色任务，头饰组合不增加两次荣誉进度。遍历所有满足条件的称号任务，已有 `achieved`／`received` 不重复达成，超过最高门槛仍可继续购买和制作。达成仅写任务状态，奖励仍通过称号任务领取接口发放；已有称号升级的资源处理限制未在此修复。

响应沿用演出称号已核验的通用形式：`updatedResources.userHonorMissions[].achievedMissionIds` 仅包含本次新达成项，Suite 持久记录为空；`userMissionStatuses` 保存达成或已领取状态。逆向 `UserDataManager.UpdateHonorMission` 按响应数组重建类型映射，因此保留完整进度列表；本次更新不重排无关的旧记录。**三类收集跨门槛行为是基于 master、现有低进度购买样本与通用称号流程的推导，尚无收集门槛的官方成功样本。** 本地检查覆盖门槛前、跨多档、领奖续增、高进度补齐、失败回滚及合唱只计一次；服装 HTTP 检查覆盖 49→50→51。既有贴图、Vocal、服装官方记录回归通过，演出称号门槛回归也通过，不将这些报告冒充收集门槛官方对拍。

Client 复用 `shop-purchase`；成功样本重放使用 `--replay-vocal-shop <record> <master> <output>`，拒绝样本使用 `--replay-vocal-rejection`。贴图拒绝另有 `--replay-stamp-rejection`。证据为 `AnotherVocalPurchaseConfirmDialog`、`PostUserShopAPI`、音源及商店 master 和官方请求及 Suite 回读。

## PATCH `/api/user/{userId}/stamp-favorite`

请求无 query，body 使用 dump 的 `UserStampFavoriteRequest`：`userStampFavoriteResource` 包含 `userStampFavoriteTabs`（`userId`、`tabNum`、`tabName`）和 `userStampFavorites`（`userId`、`stampId`、`tabNum`、`num`）。客户端离开表情设置页时提交，成功后合并 `updatedResources`。

- 客户端生成 3 个页签（0～2），每页 20 个槽位（0～19），只提交非空收藏；同一页内移动表情会清掉旧位置。
- 官方保存、重复保存、移动、清空及单页签修改样本确认：收藏列表整体替换，未提交的收藏删除；同一表情可出现在不同页签，最后一个槽位可用。
- 页签按 `tabNum` 更新，未提交的页签保留；空页签数组不能删除已有页签。名称变化时刷新完整页签列表。
- 两组资源分别判断变化，仅刷新发生变化的组；重复保存不返回这两组字段。返回记录均保留 `userId`。清空收藏不改变持有表情。
- Server 拒绝槽位越界、同位置冲突、同页重复表情、未持有表情及其他账号记录；这些输入检查尚未核验官方拒绝状态和错误体。名称长度、过滤及非法输入边界仍待样本。

Client 操作为 `stamp-favorite-save`，发送时填入当前账号，不改共享场景；页签名称在记录中脱敏。专项重放入口为 `--replay-stamp-favorite <record> <output>`，只比较收藏、页签和持有表情，其他背景字段不在核验范围内。

证据为 `PatchUserStampFavoriteAPI`、`ScreenLayerStampSetting.CreateRequest` / `OnClickItem` 的调用链、dump 请求模型与官方请求及 Suite 回读。

## PUT `/api/user/{userId}/shop/{shopId}/item/{shopItemId}`

> 审计版本: jp-6.5.5
> 关键词：区域商店，升级，商店，用户资源

更新已有区域商店项目，主要用于区域商店的升级/强化分支。它和购买接口使用同一路径，但 method 为 PUT；客户端同样不发送请求体，成功后合并用户资源差异。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `shopId`: 商店 ID。
- Path `shopItemId`: 商店项目 ID。
- Body: 无参数。客户端内部的 `UserShopRequest` 只用于保存 `shopId` / `shopItemId` 并拼 path。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据，主要用于刷新区域商店项目状态、消耗资源和相关用户资源。

### 客户端请求时机

目前确认有这些时机：

1. 区域商店详情弹窗的升级/强化分支请求。
   - 客户端根据商品详情的销售类型分支决定 POST 或 PUT。
   - 首次购买走 POST；已有项目升级/强化走 PUT。
   - PUT 成功后合并用户资源并关闭详情弹窗。

### 客户端切入点

- `Sekai.PutUserShopAPI.Execute`: 确认 path 为 `user/{userId}/shop/{shopId}/item/{shopItemId}`，method 为 PUT，request body 为空，response 为 `UserShopResponse`。
- `Sekai.PutUserShopAPI.OnCallBack`: 确认成功后合并 `response.updatedResources`。
- `Sekai.UserShopRequest`: 确认 `shopId` 和 `shopItemId` 只作为 path 参数来源。
- `Sekai.AreaShopDetailDialog.OnClickOK`: 确认区域商店根据销售类型在 POST 购买和 PUT 更新之间分支。
- `Sekai.AreaShopDetailDialog.OnFinishedPutUserShopAPI`: 区域商店 PUT 成功后的完成回调。

## POST `/api/user/{userId}/story/{storyType}/episode/{episodeId}`

> 审计版本: jp-6.5.5
> 关键词：Story，Episode，已读，奖励，用户资源

提交一个故事 episode 已读。客户端把故事类型和 episode ID 放入 path，不发送请求体；成功后合并返回的用户资源差异，并读取可能获得的故事奖励资源。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `storyType`: 故事类型，已确认包括 `unit_story`、`special_story`、`card_story`、`character_profile_story`、`event_story`、`archive_event_story`。
- Path `episodeId`: 故事 episode ID。
- Body: 无参数。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后合并到本地用户数据，常见影响为各类 episode status、卡牌故事状态和相关奖励状态。卡牌 side story 首读样本中会返回 `userCards` 和 `userChargedCurrency`。
- `obtainedResources`: 本次读完 episode 后获得的资源数组；客户端用于奖励展示或后续奖励弹窗判断。卡牌 side story 首读样本中按 master `episode_reward` 资源盒返回 `jewel`，前篇常见为 25，后篇常见为 50。

### 客户端请求时机

目前确认有这些时机：

1. 主线/活动/归档活动等故事 episode 播放完成时请求。
   - 客户端在故事播放结束后提交对应 `storyType` 和 `episodeId`。
   - 请求成功后合并 `updatedResources`，再进入奖励展示、列表刷新或场景退出流程。

2. 卡牌 side story 或角色档案故事读完时请求。
   - 卡牌故事通常会先经过解锁/确认流程，再在实际读完后请求本接口。
   - 卡牌 side story 成功后客户端把对应 episode 合并为 `already_read`，并处理 `obtainedResources` 中的首读奖励。
   - 客户端随后还会提交 `/log` 播放日志；抓包显示该日志请求通常不重复发放首读奖励。

3. 新手/登录故事流程中也会复用同一 API。
   - 客户端在特定剧情播放完成后提交 episode 已读。
   - 成功后继续 tutorial、home 或登录后续流程。

### 客户端切入点

- `Sekai.PostUserStoryAPI.Execute`: 确认 path 为 `user/{userId}/story/{storyType}/episode/{episodeId}`，method 为 POST，request 为 `EmptyRequest`，response 为 `UserStoryResponse`。
- `Sekai.PostUserStoryAPI.OnCallBack`: 确认成功后会合并 `response.updatedResources`。
- `Sekai.UserStoryResponse`: 确认 response 字段为 `updatedResources` 和 `obtainedResources`。
- `Sekai.ScreenLayerStorySelectBase.OnFinishedEpisodeAPI`: 普通故事选择页读完 episode 后的处理入口。
- `Sekai.ScreenLayerEventTop.OnFinishedEpisodeAPI`: 活动首页播放故事后的处理入口。
- `Sekai.ScreenLayerCardDetail.OnFinishedEpisodeAPI` / `Sekai.SideStoryCell.OnFinishedEpisodeAPI`: 卡牌故事读完后的处理入口。
- `Sekai.CharacterProfileContent.OnFinishedEpisodeAPI`: 角色档案故事读完后的处理入口。

## POST `/api/user/{userId}/story/{storyType}/episode/{episodeId}/cost`

> 审计版本: jp-6.5.5
> 关键词：Story，卡牌故事，解锁，消耗，用户资源

提交故事 episode 解锁消耗。客户端主要在卡牌 side story 的锁定 episode 解锁流程中请求，用指定消耗类型解锁后续故事；成功后合并资源差异并继续读故事确认流程。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `storyType`: 故事类型；当前确认的主要使用场景为 `card_story`。
- Path `episodeId`: 要解锁的 episode ID。
- Body `cardEpisodeReleaseCostType`: 解锁消耗类型，已确认有 `common_material` 和 `card_episode_release_ticket`。已确认样本实际发送 `common_material`。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后合并，用于刷新 episode 解锁状态和资源持有状态。卡牌 side story 样本中会返回对应卡牌 episode 的 `scenarioStatus = released`，并返回扣减后的 `userMaterials`。
- `consumedResources`: 本次解锁消耗的资源数组；客户端用于消耗展示或后续 UI 刷新。`common_material` 使用卡牌 episode master 的 `costs`；`card_episode_release_ticket` 使用配置中的放开券材料 ID 和数量。

### 客户端请求时机

目前确认有这些时机：

1. 卡牌详情页解锁 side story 时请求。
   - 玩家在锁定 episode 的解锁确认弹窗中选择消耗方式。
   - 客户端提交 `cardEpisodeReleaseCostType`，成功后合并 `updatedResources`。
   - 随后客户端继续打开读故事确认弹窗或刷新卡牌详情状态；实际链路中紧接着会请求 `POST /api/user/{userId}/story/card_story/episode/{episodeId}`。

2. Side Story 列表单元中解锁卡牌故事时请求。
   - 列表单元同样按当前卡牌 episode 和消耗方式构造请求。
   - 请求成功后进入后续读故事确认/播放流程。

### 客户端切入点

- `Sekai.PostUserStoryCostAPI.Execute`: 确认 path 为 `user/{userId}/story/{storyType}/episode/{episodeId}/cost`，method 为 POST，request 为 `UserStoryRequest`，response 为 `UserStoryCostResponse`。
- `Sekai.UserStoryRequest`: 确认 request body 字段为 `cardEpisodeReleaseCostType`。
- `Sekai.UserStoryCostResponse`: 确认 response 字段为 `updatedResources` 和 `consumedResources`。
- `Sekai.ScreenLayerCardDetail.ExecuteEpisodeCostAPI` / `OnFinishedEpisodeCostAPI`: 卡牌详情页解锁 side story 的请求和完成入口。
- `Sekai.SideStoryCell.ExecuteEpisodeCostAPI` / `OnFinishedEpisodeCostAPI`: Side Story 列表单元解锁入口。

## POST `/api/user/{userId}/story/{storyType}/episode/{episodeId}/log`

> 审计版本: jp-6.5.5
> 关键词：Story，播放日志，跳过，自动播放，奖励

提交故事播放日志。客户端在 story 播放结束后把本次播放行为信息提交给服务端，包括是否跳过、是否自动播放、页数、连续播放状态和故事内 MV/音乐播放信息；成功后合并用户资源并处理可能获得的资源结果。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `storyType`: 故事类型。
- Path `episodeId`: 故事 episode ID。
- Body `noSkip`: 是否未跳过。
- Body `useSkip`: 是否使用跳过。
- Body `autoFinish`: 是否自动结束。
- Body `useAuto`: 是否使用自动播放。
- Body `fastForward`: 是否快进。
- Body `voice`: 是否播放语音。
- Body `numPages`: 本次故事页数。
- Body `continuousPlayStart`: 是否连续播放起始。
- Body `playMusicVideo`: 是否播放故事内 MV。
- Body `musicVocalId`: 故事内 MV 使用的 vocal ID；未使用时为 0。
- Body `musicCategoryName`: 音乐类别名；抓包中未播放 MV 时仍可为 `mv`。
- Body `musicVideoNoSkip`: 故事内 MV 是否未跳过。
- Body `userStoryMusicPlays`: 故事内音乐播放数组，元素包含 `musicId` 和 `musicTrackType`。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后合并到本地用户数据。已确认的卡牌 side story 日志样本只返回通用刷新字段，没有再次返回 `userCards` 或奖励资源。
- `userObtainResourceResults`: 本次日志提交后获得的资源结果数组；元素包含 `obtainReason` 和 `userResources`。已确认的卡牌 side story 日志样本为空数组。

### 客户端请求时机

目前确认有这些时机：

1. 普通故事连续播放器在 episode 播放结束后请求。
   - 客户端先完成故事播放，再根据本次播放设置构造日志请求。
   - 请求成功后合并 `updatedResources`，并继续下一话、奖励展示或退出流程。

2. 活动故事、活动首页开场故事和卡牌 side story 播放结束后请求。
   - 这些场景会在自己的故事结束回调中提交同类日志。
   - 卡牌 side story 的实际链路为先 `/cost` 解锁，再提交 episode 完成并领取首读奖励，最后提交 `/log` 播放日志。
   - 成功后继续各自页面的刷新、奖励弹窗或场景退出。

3. 角色档案故事播放结束后也会请求。
   - 客户端用同一类日志模型提交播放行为。
   - 成功后继续角色档案故事退出流程。

### 客户端切入点

- `Sekai.PostUserStoryLogAPI.Execute`: 确认 path 为 `user/{userId}/story/{storyType}/episode/{episodeId}/log`，method 为 POST，request 为 `UserStoryLogRequest`，response 为 `UserStoryLogResponse`。
- `Sekai.UserStoryLogRequest`: 确认播放日志字段为 `noSkip`、`useSkip`、`autoFinish`、`useAuto`、`fastForward`、`voice`、`numPages`、`continuousPlayStart`、`playMusicVideo`、`musicVocalId`、`musicCategoryName`、`musicVideoNoSkip`、`userStoryMusicPlays`。
- `Sekai.UserStoryLogResponse`: 确认 response 字段为 `updatedResources` 和 `userObtainResourceResults`。
- `Sekai.ConsecutiveScenarioPlayer.StoryEndLogCallBack`: 连续播放器提交故事日志的通用入口。
- `Sekai.ScreenLayerStorySelectBase.StoryEndLogCallBack`: 普通故事选择页的日志提交入口。
- `Sekai.ScreenLayerEventStorySelect.StoryEndLogCallBack` / `Sekai.ScreenLayerEventTop.StoryEndLogCallBack`: 活动故事相关日志提交入口。
- `Sekai.ScreenLayerCardDetail.StoryEndLogCallBack` / `Sekai.SideStoryCell.StoryEndLogCallBack`: 卡牌故事日志提交入口。
- `Sekai.CharacterProfileContent.StoryEndLogCallBack`: 角色档案故事日志提交入口。

## GET `/api/user/{userId}/story/recommend`

> 审计版本: jp-6.5.5
> 关键词：Story，推荐，故事分类，首页

获取故事推荐列表。客户端用它在故事分类/推荐入口展示可继续阅读、主线、推荐或收藏相关的故事卡片。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body: 无参数。

### 返回字段

- `userStoryRecommends`: 推荐故事数组。
- `userStoryRecommends[].storyType`: 推荐故事类型。
- `userStoryRecommends[].storyId`: 推荐故事 ID。
- `userStoryRecommends[].reason`: 推荐原因，例如 `continuously`、`recommend`、`main_story` 等。
- `userStoryRecommends[].category`: 推荐分类。
- `userStoryRecommends[].seq`: 展示顺序。

### 客户端请求时机

目前确认有这些时机：

1. 故事分类选择页加载推荐内容时请求。
   - 客户端进入故事分类/推荐入口后请求推荐列表。
   - 请求成功后用 `userStoryRecommends` 构建推荐展示项。

2. 推荐列表刷新或回调处理中使用同一响应。
   - 客户端把推荐结果写入页面状态。
   - 后续点击推荐项会进入对应故事选择或播放流程；点击上报另有独立接口。

### 客户端切入点

- `Sekai.GetUserStoryRecommendAPI.Execute`: 确认 path 为 `user/{userId}/story/recommend`，method 为 GET，request 为 `EmptyRequest`，response 为 `UserStoryRecommendResponse`。
- `Sekai.UserStoryRecommendResponse`: 确认 response 字段为 `userStoryRecommends`。
- `Sekai.ScreenLayerStoryCategorySelect.OnGetUserStoryRecommendAsync` / `OnApiCallBack`: 故事分类页请求推荐和处理推荐响应的入口。

## GET `/api/user/{userId}/story-favorite/friend/status/{storyType}`

> 审计版本: jp-6.5.5
> 关键词：Story，收藏，好友，状态

获取好友对某类故事的收藏状态。客户端用它在故事收藏/好友相关 UI 中判断哪些故事存在好友收藏状态，便于展示好友收藏提示或相关入口。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `storyType`: 故事类型。
- Body: 无参数。

### 返回字段

- `friendStoryFavoriteStatuses`: 好友故事收藏状态数组；元素结构用于表示好友对故事的收藏状态，当前报告只确认客户端按数组消费。

### 客户端请求时机

目前确认有这些时机：

1. 故事收藏或好友收藏状态需要展示时请求。
   - 客户端按当前故事类型发起请求。
   - 请求成功后把 `friendStoryFavoriteStatuses` 用于后续列表/入口状态显示。

### 客户端切入点

- `Sekai.StoryFavorite.GetFriendStoryFavoriteStatusesAPI.Execute`: 确认 path 为 `user/{userId}/story-favorite/friend/status/{storyType}`，method 为 GET，request 为 `EmptyRequest`，response 为 `GetFriendStoryFavoriteStatusesResponse`。
- `Sekai.StoryFavorite.GetFriendStoryFavoriteStatusesResponse`: 确认 response 字段为 `friendStoryFavoriteStatuses`。

## GET `/api/user/{userId}/story-episode-bookmark/{storyType}/story/{storyId}`

> 审计版本: jp-6.5.5
> 关键词：Story，书签，Talk，缩略图

获取指定故事的 episode 书签列表。客户端用它恢复某个故事下已保存的 talk/episode 书签，后续新增、编辑、点击统计分别走其他书签相关接口。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `storyType`: 故事类型。
- Path `storyId`: 故事 ID。
- Body: 无参数。

### 返回字段

- `userStoryEpisodeBookmarks`: 书签数组。
- `userStoryEpisodeBookmarks[].storyId`: 书签所属故事 ID。
- `userStoryEpisodeBookmarks[].storyEpisodeId`: 书签所属 episode ID。
- `userStoryEpisodeBookmarks[].talkId`: 书签定位的 talk ID。
- `userStoryEpisodeBookmarks[].name`: 书签名称。
- `userStoryEpisodeBookmarks[].thumbnailPath`: 书签缩略图路径。
- `userStoryEpisodeBookmarks[].createdAt`: 创建时间。
- `updatedResources`: 可选的用户资源差异；客户端模型包含该字段。

### 客户端请求时机

目前确认有这些时机：

1. 打开支持 episode 书签的故事时请求。
   - 客户端按当前 `storyType` 和 `storyId` 拉取已有书签。
   - 请求成功后在故事播放/选择界面恢复书签列表或书签入口状态。

2. 书签后续操作会使用其他相关接口。
   - 当前接口只负责拉取已有书签列表。
   - 新增/编辑 talk 书签和点击统计使用带 `episode/{episodeId}/talk/{talkId}` 的路径。

### 客户端切入点

- `Sekai.GetStoryEpisodeBookmarkAPI.Execute`: 确认 path 为 `user/{userId}/story-episode-bookmark/{storyType}/story/{storyId}`，method 为 GET，request 为 `EmptyRequest`，response 为 `StoryEpisodeBookmarkResponse`。
- `Sekai.StoryEpisodeBookmarkResponse`: 确认 response 字段为 `userStoryEpisodeBookmarks` 和 `updatedResources`。
- `Sekai.UserStoryEpisodeBookmark`: 确认书签字段包括 `storyId`、`storyEpisodeId`、`talkId`、`name`、`thumbnailPath`、`createdAt`。

## GET `/api/user/{userId}/present/history`

> 审计版本: jp-6.5.5
> 关键词：礼物邮箱，领取历史，用户资源

获取当前用户的礼物领取历史。客户端进入礼物邮箱内容时会拉取历史记录，并和本地已有的 `userPresents` 一起构建礼物列表和历史页。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body: 无参数。

### 返回字段

- `userPresentHistories`: 已领取礼物历史列表。
- `userPresentHistories[].presentId`: 礼物 ID。
- `userPresentHistories[].seq`: 展示/排序用序号。
- `userPresentHistories[].resourceType`: 礼物资源类型。
- `userPresentHistories[].resourceId`: 礼物资源 ID。
- `userPresentHistories[].resourceLevel`: 礼物资源等级。
- `userPresentHistories[].resourceQuantity`: 礼物资源数量。
- `userPresentHistories[].expiredAt`: 礼物过期时间。
- `userPresentHistories[].receivedAt`: 领取时间。
- `userPresentHistories[].reason`: 礼物来源说明。

### 客户端请求时机

目前确认有这些时机：

1. 进入礼物邮箱内容初始化流程时请求。
   - 客户端通过礼物邮箱数据服务拉取领取历史。
   - 请求成功后保存 `userPresentHistories`，再从本地用户数据取当前未领取的 `userPresents`。
   - 随后把未领取礼物和历史记录组合成 `UserPresent`，用于展示礼物页和历史页。

2. 礼物领取后刷新礼物邮箱内容时会再次请求。
   - 单个领取或全部领取成功并展示奖励结果后，客户端会重新执行礼物邮箱内容加载流程。
   - 这会重新拉取领取历史，并刷新礼物列表当前索引。

### 客户端切入点

- `Sekai.GetUserPresentHistoriesAPI.Execute`: 确认 path 为 `user/{userId}/present/history`，method 为 GET，request 为 `EmptyRequest`，response 为 `UserPresentHistoriesResponse`。
- `Sekai.GetUserPresentHistoriesAPI.OnCallBack`: 确认 API 层只转发回调。
- `Sekai.Service.PresentDataService.Load`: 礼物邮箱历史加载服务入口，执行 `GetUserPresentHistoriesAPI` 并等待完成。
- `Sekai.Service.PresentDataService.OnFinishPresentHistoriesAPI`: 成功时保存 `UserPresentHistoriesResponse`。
- `Sekai.PresentContent.Execute`: 进入礼物邮箱内容时拉取历史，并用本地 `userPresents` 加历史记录构建展示数据。
- `Sekai.PresentHistoryView.Show`: 礼物历史页使用已加载的 `userPresentHistories` 展示列表。

## POST `/api/user/{userId}/present`

> 审计版本: jp-6.5.5
> 关键词：礼物邮箱，领取，奖励，用户资源

领取一个或多个礼物。客户端把要领取的 `presentIds` 提交给服务端，成功后合并返回的用户资源差异，并用 `receivedUserPresents` 展示奖励结果。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `presentIds`: 要领取的礼物 ID 列表。单个领取时只包含一个 ID；全部领取时包含当前可领取礼物的所有 ID。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据，主要用于移除已领取礼物并增加获得的资源。
- `receivedUserPresents`: 本次成功领取的礼物列表。
- `receivedUserPresents[].presentId`: 礼物 ID。
- `receivedUserPresents[].seq`: 展示/排序用序号。
- `receivedUserPresents[].resourceType`: 礼物资源类型。
- `receivedUserPresents[].resourceId`: 礼物资源 ID。
- `receivedUserPresents[].resourceLevel`: 礼物资源等级。
- `receivedUserPresents[].resourceQuantity`: 礼物资源数量。
- `receivedUserPresents[].expiredAt`: 礼物过期时间。
- `receivedUserPresents[].reason`: 礼物来源说明。

### 客户端请求时机

目前确认有这些时机：

1. 邮箱中点击单个礼物领取时请求。
   - 客户端把当前点击的 `presentId` 包装成单元素 `presentIds`。
   - 请求成功后合并 `updatedResources`。
   - 随后取 `receivedUserPresents[0]` 的资源信息展示奖励弹窗，并在弹窗结束后重新加载礼物邮箱内容。

2. 礼物邮箱中点击全部领取时请求。
   - 客户端从当前 `userPresents` 里筛选可领取项，取它们的 `presentId` 组成 `presentIds`。
   - 请求成功后合并 `updatedResources`。
   - 随后用 `receivedUserPresents` 展示批量奖励结果，并在弹窗结束后重新加载礼物邮箱内容。

3. 歌曲解锁相关弹窗会从礼物邮箱领取指定歌曲礼物。
   - 客户端在本地 `userPresents` 中筛选 `resourceType=music` 且 `resourceId` 等于目标歌曲 ID 的礼物。
   - 命中后提交该礼物 ID。
   - 请求成功后合并 `updatedResources`，并展示歌曲领取奖励弹窗；未命中时只记录错误，不发领取请求。

### 客户端切入点

- `Sekai.PostUserPresentAPI.PostUserPresentAPI`: 确认 request body 为 `UserPresentAPIRequest.presentIds`。
- `Sekai.PostUserPresentAPI.Execute`: 确认 path 为 `user/{userId}/present`，method 为 POST，request 为 `UserPresentAPIRequest`，response 为 `UserPresentReceiveResponse`。
- `Sekai.PostUserPresentAPI.OnCallBack`: 确认 API 层只转发回调。
- `Sekai.Service.PresentDataService.Receive`: 礼物领取服务入口，执行 `PostUserPresentAPI` 并等待完成。
- `Sekai.Service.PresentDataService.OnFinishPresentReceiveAPI`: 成功时保存 `UserPresentReceiveResponse`，并合并 `response.updatedResources`。
- `Sekai.PresentContent.OnClickReceive`: 单个礼物领取入口，构造单元素 `presentIds`。
- `Sekai.PresentContent.OnClickPresentAllReceiveButton`: 全部领取入口，筛选可领取礼物后提交多个 `presentIds`。
- `Sekai.PresentContent.OnClickReceiveCallBack`: 领取奖励弹窗结束后重新加载礼物邮箱内容。
- `Sekai.MusicDialogUtility.ReceiveMusicFromPresentBox`: 指定歌曲礼物领取入口，按 `musicId` 从礼物邮箱中查找并领取对应礼物。

## PUT `/api/user/{userId}/card`

> 审计版本: jp-6.5.5
> 关键词：卡牌，转换，等待室，素材

执行等待室(休息室)卡牌转换。客户端把确认转换的卡牌汇总成 `userCards` 发给服务端，请求成功后合并返回的用户资源差异，并刷新等待室显示。

### 请求参数

- Path `userId`: 当前用户 ID。
- Query `behavior`: 固定为 `exchange`。
- Body `userCards`: `UserCard[]`，待转换的卡牌列表。客户端会从确认弹窗传入的卡牌列表汇总后发送。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据。客户端成功后会合并到本地用户数据，主要用于刷新 `userCards` 和转换获得的资源数量。

### 客户端请求时机

目前确认有这些时机：

1. 等待室卡牌转换流程中，用户选择卡牌并在确认弹窗点确定后请求。
   - 客户端先根据选择结果构造 `UserCardExchangeRequest.userCards`。
   - 请求成功后合并 `updatedResources`。
   - 随后展示转换结果弹窗，并重新初始化等待室卡牌列表和素材数量显示。

### 客户端切入点

- `Sekai.PutUserCardExchangeAPI.Execute`: 确认 path 为 `user/{userId}/card/?behavior=exchange`，method 为 PUT，request 为 `UserCardExchangeRequest`，response 为 `UserCardExchangeResponse`。
- `Sekai.PutUserCardExchangeAPI.OnCallBack`: 确认成功后合并 `response.updatedResources`。
- `Sekai.ScreenLayerWaitingRoom.OnClickConfirmOK`: 等待室确认转换后构造 request 并执行 API。
- `Sekai.ScreenLayerWaitingRoom.OnFinishedPutUserCardExchangeAPI`: 转换成功后显示结果弹窗，并刷新等待室状态。

## POST `/api/user/{userId}/card/{cardId}/practice-ticket`

> 审计版本: jp-6.5.5
> 关键词：卡牌，练习，经验，练习券

使用练习券提升指定卡牌经验。客户端把目标卡牌 ID 放入 path，把实际消耗的练习券列表放入 `costs`，请求成功后播放经验增长结果并合并用户资源差异。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `cardId`: 要提升经验的卡牌 ID。
- Body `costs`: `UserResource[]`，本次消耗的练习券资源列表。
- Body `costs[].resourceType`: 固定为 `practice_ticket`。
- Body `costs[].resourceId`: 练习券 ID。
- Body `costs[].resourceLevel`: 资源等级，已确认样本为 `0`。
- Body `costs[].quantity`: 消耗数量。

### 返回字段

- `updateExpResult`: 卡牌经验变化结果。
- `updateExpResult.beforeTotalExp`: 请求前卡牌总经验。
- `updateExpResult.afterTotalExp`: 请求后卡牌总经验。
- `updateExpResult.beforeExp`: 请求前当前等级内经验。
- `updateExpResult.afterExp`: 请求后当前等级内经验。
- `updateExpResult.beforeLevel`: 请求前卡牌等级。
- `updateExpResult.afterLevel`: 请求后卡牌等级。
- `updatedResources`: `SuiteUser` 局部更新数据。客户端成功后会合并到本地用户数据，关注 `userCards`、`userPracticeTickets` 和相关任务状态。

### 客户端请求时机

目前确认有这些时机：

1. 卡牌练习界面确认使用练习券后请求。
   - 客户端根据选择数量构造 `costs`。
   - 请求成功后合并 `updatedResources`。
   - 随后使用 `updateExpResult` 播放等级和经验增长结果。

### 客户端切入点

- `Sekai.PostUserCardPracticeTicketAPI.Execute`: 确认 path 为 `user/{userId}/card/{cardId}/practice-ticket`，method 为 POST，request 为 `UserCardPracticeTicketRequest`，response 为 `UserCardPracticeTicketResponse`。
- `Sekai.UserCardPracticeTicketRequest`: 确认 request body 字段为 `costs`。
- `Sekai.UserCardPracticeTicketResponse`: 确认 response 字段为 `updateExpResult` 和 `updatedResources`。

## POST `/api/user/{userId}/card/{cardId}/master-lesson`

> 审计版本: jp-6.5.5
> 关键词：卡牌，Master Rank，Master Lesson，奖励

提升指定卡牌的 Master Rank。客户端把目标卡牌 ID 放入 path，把确认消耗的 `masterLessonCostIds` 放入 body；请求成功后合并用户资源差异，并展示本次获得的 Master Lesson 奖励。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `cardId`: 要提升 Master Rank 的卡牌 ID。
- Body `masterLessonCostIds`: `int[]`，本次消耗的 Master Lesson cost ID 列表。

### 返回字段

- `obtainedRewards`: 本次 Master Lesson 发放的奖励数组。
- `obtainedRewards[].masterLessonRewardId`: Master Lesson 奖励 ID。
- `obtainedRewards[].obtainRewards`: `UserResource[]`，该奖励 ID 对应的获得资源。
- `updatedResources`: `SuiteUser` 局部更新数据。客户端成功后会合并到本地用户数据，关注 `userCards`、消耗素材和获得服装等资源。

### 客户端请求时机

目前确认有这些时机：

1. 卡牌 Master Lesson 确认消耗后请求。
   - 客户端根据目标卡牌、目标 Master Rank 和选择消耗构造 `masterLessonCostIds`。
   - 请求成功后合并 `updatedResources`。
   - 随后用 `obtainedRewards` 展示获得奖励，并刷新卡牌培养状态。

### 客户端切入点

- `Sekai.PostUserCardMasterLessonAPI.Execute`: 确认 path 为 `user/{userId}/card/{cardId}/master-lesson`，method 为 POST，request 为 `PostUserCardMasterLessonAPIRequest`，response 为 `UserCardMasterLessonResponse`。
- `Sekai.PostUserCardMasterLessonAPIRequest`: 确认 request body 字段为 `masterLessonCostIds`。
- `Sekai.UserCardMasterLessonResponse`: 确认 response 字段为 `obtainedRewards` 和 `updatedResources`。

## PUT `/api/user/{userId}/card/{cardId}?behavior=special_training`

> 审计版本: jp-6.5.5
> 关键词：卡牌，特训，状态，立绘

提交指定卡牌的特训状态。客户端把目标卡牌 ID 放入 path，把特训状态放入 body；请求成功后合并用户资源差异，并进入特训完成后的卡牌展示流程。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `cardId`: 要更新特训状态的卡牌 ID。
- Query `behavior`: 固定为 `special_training`。
- Body `specialTrainingStatus`: 特训状态，已确认样本为 `done`。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据。客户端成功后会合并到本地用户数据，关注 `userCards`。

### 客户端请求时机

目前确认有这些时机：

1. 卡牌特训确认完成后请求。
   - 客户端发送 `specialTrainingStatus`。
   - 请求成功后合并 `updatedResources`。
   - 随后刷新卡牌详情和特训后展示状态。

### 客户端切入点

- `Sekai.PutUserCardSpecialTrainingAPI.Execute`: 确认 path 为 `user/{userId}/card/{cardId}?behavior=special_training`，method 为 PUT，request 为 `UserCardSpecialTrainingRequest`，response 为 `SuiteUserCommonResponse`。
- `Sekai.UserCardSpecialTrainingRequest`: 确认 request body 字段为 `specialTrainingStatus`。

后续官方样本：三星卡在 40 级完成特训后，等级及累计经验不变，`specialTrainingStatus=done`，`defaultImage=special_training`；材料按逐卡配置扣除。所属角色 `collect_member` 进度增加 1，新达成项同时出现在 `userCharacterMissionV2Statuses` 和响应进度项的 `achievedMissions` 中；后者不保留到后续 Suite 状态。无特殊奖励的样本额外返回 `obtainedResources: []`，该字段不在旧 `SuiteUserCommonResponse` 内。

Server 已补卡面和任务联动，Client 支持 `--replay-special-training`。该样本的相关响应与增量一致；导入基线仍有一项无关 Live 任务的 `userId` 字段差异。随后对已特训卡再次请求，官方返回 HTTP 409；重新认证回读确认卡牌、材料、角色任务及服装状态不变。Server 已对齐重复请求的状态码和无副作用行为，错误正文未核验。非空特殊奖励及其他稀有度尚需官方样本；非空奖励返回字段不按空数组样本推定。`special-training.json` 中的卡牌须替换为目标账号持有、未特训且已达等级上限的卡，并准备 master 指定材料。

## PUT `/api/user/{userId}/card/{cardId}?behavior=set_default_image`

> 审计版本: jp-6.5.5
> 关键词：卡牌，默认立绘，特训立绘，显示

设置指定卡牌的默认显示立绘。客户端把目标卡牌 ID 放入 path，把目标立绘类型放入 body；请求成功后合并用户资源差异，并刷新卡牌详情显示。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `cardId`: 要更新默认立绘的卡牌 ID。
- Query `behavior`: 固定为 `set_default_image`。
- Body `defaultImage`: 默认立绘类型，已确认样本包括 `original` 和 `special_training`。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据。客户端成功后会合并到本地用户数据，关注 `userCards`。

### 客户端请求时机

目前确认有这些时机：

1. 卡牌详情中切换默认显示立绘后请求。
   - 客户端发送 `defaultImage`。
   - 请求成功后合并 `updatedResources`。
   - 随后刷新当前卡牌详情图像状态。

### 客户端切入点

- `Sekai.PutUserCardDefaultImageAPI.Execute`: 确认 path 为 `user/{userId}/card/{cardId}?behavior=set_default_image`，method 为 PUT，request 为 `UserCardDefaultImageRequest`，response 为 `SuiteUserCommonResponse`。
- `Sekai.UserCardDefaultImageRequest`: 确认 request body 字段为 `defaultImage`。

### 官方核验与实现

已特训卡牌切回 `original`、恢复 `special_training`、同值保存三个样本均返回 200 和顶层 `obtainedResources=[]`。改变卡面时返回 `userCards`；同值保存时不返回该字段。相关卡牌、材料及任务状态未出现额外变化，HTTP 重放对拍一致。

`ScreenLayerCardDetail.CheckAndExecuteDefaultImageAPI` 比较当前显示与进入页面时的备份，仅在发生切换时发送。Server 只修改已持有卡牌，并限制上述两种卡面值；未持有卡牌、非法值及未特训卡牌请求特训卡面的官方失败行为仍待核验。Client 已有 `card-default-image`，新增 `--replay-card-image` 对拍入口。

## GET `/api/user/{userId}/story-favorite/comment`

读取当前用户的剧情评论，无 query 和 body。返回 `GetStoryFavoriteCommentResponse.userStoryFavorites`，元素为 `UserStoryComment`，字段包括 `storyType`、`storyId`、`comment`、`isSpoiler`，没有 `shareNo`；不能与 Suite 中的收藏槽位记录混用。

证据为 `GetStoryFavoriteCommentAPI.Execute`、路径字符串及响应模型。自建账号尚未评论时，官方返回 200 和 `userStoryFavorites=[]`。Client 已提供 `favorite-comment-list`；非空记录及首次评论与收藏联动仍待核验，Server 尚未接入该接口。

## PUT `/api/user/{userId}/character/{characterId}/mission[/{characterMissionType}]`

- Path：当前 `userId`、角色 `characterId`；不带类型表示领取该角色全部可领奖任务。指定类型使用 `CharacterMissionType` 的大写名称，`other` 特例映射为小写 `achievement`。
- 无 query 和 body。客户端在角色任务页领取时调用，成功后合并用户数据并展示领取及等级变化。
- Response：`UserCharacterMissionV2Response`，含 `updatedResources` 和 `reportedMissionStatuses`。已核验的报告项为本次领取的状态，包含 `missionId`、`parameterGroupId`、`seq`、`characterId`、`missionStatus=received`，不带 `userId`。
- 官方 `COLLECT_COSTUME_3D` 样本一次领取 8 条达成项，按参数组每条 1 点增加角色累计经验 8，角色等级 3 → 6。按各等级奖励箱累计获得免费宝石 400、材料 44×1、称号 1（等级 1）、称号背景和文字各 10101；后续 Suite 回读持久化。没有改动角色任务进度，也没有新增邮箱礼物。

Client 已提供 `character-mission-receive`、`character-mission-receive-all` 和 `--replay-character-mission`。Server 已接入服装收集、角色档案语音收集及这两类任务的全部领取，经验与等级奖励走共用角色服务，称号、背景及文字走资源处理器。全部领取样本合并 7 条服装和 1 条语音任务，角色累计经验 0 → 8、等级 1 → 5、等级内经验 1，获得免费宝石 500 和该角色首次称号资源；报告只列本次任务。指定类型与全部领取样本的相关 HTTP、基线、响应和状态增量一致。事务失败不保留已发奖励或任务状态。

指定服装类型和全部领取在没有新达成项时均返回 200、`reportedMissionStatuses=[]`，仍刷新 `userCharacters`，不刷新任务状态和奖励资源。官方重复领取前后角色经验、任务状态及奖励库存不变，两类样本的相关字段 HTTP 对拍一致。

另已接入并完成指定类型 HTTP 对拍：

- `COLLECT_MEMBER`：领取 4 条任务，每条按 master 增加 2 点经验，角色等级 1 → 5，累计经验 0 → 8。
- `READ_CARD_EPISODE_FIRST`：领取前篇任务增加 1 点经验，样本未升级。
- `READ_CARD_EPISODE_SECOND`：领取后篇任务增加 1 点经验，角色等级 3 → 4，累计经验 3 → 4，并发放等级奖励。
- `AREA_ITEM_LEVEL_UP_CHARACTER`：领取 2 条区域道具任务，共增加 2 点经验，样本未升级。
- `PLAY_LIVE`：领取首条队长 Live 次数任务增加 1 点经验，角色等级 1 → 2，累计经验 0 → 1，并发放等级奖励。
- `WAITING_ROOM`：领取首条休息室收集任务增加 1 点经验，累计经验 8 → 9，样本未升级。
- `COLLECT_ANOTHER_VOCAL`：首次收集任务按 master 增加 1 点经验，角色等级 4 → 5，累计经验 6 → 7，并发放等级奖励；指定类型重复领取返回空报告且不重复发奖。首次和重复样本的专项 HTTP 对拍一致。

上述样本均回读确认任务已领取，相关基线、响应和状态增量一致；对拍范围为角色、任务和奖励资源，不包含无关背景字段。其余任务类型、已有称号升级及满角色等级仍待核验；全部领取遇到未核验类型或缺失定义的达成项时整次拒绝，不静默跳过。`reportedMissionStatuses` 省略 `userId`，`updatedResources.userCharacterMissionV2Statuses` 保留。称号与名片的所属用户字段已补入模型构建副本；角色记录省略 `userId`，leader 名片省略零值 `profileImageId`。

证据：`PutUserCharacterMissionReceiveAPI` 的两个构造、Execute 与回调，`ScreenLayerCharacterRankMission.ExecuteApi`、响应契约、角色任务参数和等级奖励 master，以及官方指定类型领取与 Suite 回读样本。

## PUT `/api/user/{userId}/mission/beginner_mission_v2`

> 审计版本: jp-6.5.5
> 关键词：任务，新手任务，领取，奖励

领取 Beginner Mission V2 任务奖励。客户端把一个或多个任务 ID 放入 `missionIds`，请求成功后合并用户资源差异，并用返回的奖励列表展示领取结果。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `missionIds`: `int[]`，本次领取奖励的 Beginner Mission V2 任务 ID 列表。
- Body `eventMissionSelectableRewardId`: 事件任务可选奖励 ID；该 mission type 的已确认样本为 `0`。
- Body `isClosedEventMissionSelectableReward`: 是否关闭事件任务可选奖励；该 mission type 的已确认样本为 `false`。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据。客户端成功后会合并到本地用户数据，关注 `userBeginnerMissionV2s`、`userMissionStatuses` 和奖励资源。
- `obtainedRewards`: `UserResource[]`，本次领取获得的奖励资源。

### 客户端请求时机

目前确认有这些时机：

1. Mission 界面领取 Beginner Mission V2 奖励时请求。
   - 客户端把待领取任务 ID 放入 `missionIds`。
   - 请求成功后合并 `updatedResources`。
   - 随后展示 `obtainedRewards`，并刷新任务列表状态。

### 客户端切入点

- `Sekai.PutUserMissionReceiveAPI.Execute`: 确认 mission receive API 会按 `MissionType` 拼出 `user/{userId}/mission/{missionType}`，method 为 PUT。
- `Sekai.MissionType`: 确认 mission type 包括 `beginner_mission_v2`。
- `Sekai.UserMissionReceiveRequest`: 确认 request body 字段为 `missionIds`、`eventMissionSelectableRewardId`、`isClosedEventMissionSelectableReward`。
- `Sekai.UserMissionReceiveResponse`: 确认 response 字段为 `updatedResources` 和 `obtainedRewards`。

## PUT `/api/user/{userId}/custom-profile/{customProfileId}`

> 审计版本: jp-6.5.5
> 关键词：自定义名片，名称，排序，保存

保存自定义名片的名称和卡片排序。客户端改名、重排卡片、或统一保存名片状态时都会走这个接口，成功后合并用户自定义名片相关的资源差异。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `customProfileId`: 自定义名片 ID。
- Body `name`: 自定义名片名称。
- Body `customProfileCardOrders`: `UserCustomProfileCardOrder[]`，卡片排序列表。
- Body `customProfileCardOrders[].customProfileId`: 自定义名片 ID。
- Body `customProfileCardOrders[].customProfileCardId`: 名片卡 ID。
- Body `customProfileCardOrders[].seq`: 卡片顺序。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据。客户端成功后会合并到本地用户数据，关注 `userCustomProfiles`、`userCustomProfileCards` 等自定义名片状态。

### 客户端请求时机

目前确认有这些时机：

1. 自定义档案选择页修改名片名称后请求。
   - 客户端会先检查名称非空和 NG word。
   - 检查通过后带当前卡片排序一起保存。

2. 自定义名片选择页拖拽或交换卡片顺序后请求。
   - 客户端生成新的 `customProfileCardOrders`。
   - 请求成功后使用返回资源刷新本地排序状态。

3. 自定义名片相关流程需要统一保存档案名称和卡片顺序时请求。
   - 通用封装会传入 `name`、`orders` 和是否显示加载指示。
   - 请求完成后返回布尔结果给外层 UI 流程。

### 客户端切入点

- `Sekai.CustomProfile.PutUserUpdateProfileAPI.Execute`: 确认 path 为 `user/{userId}/custom-profile/{customProfileId}`，method 为 PUT，request 为 `UserSaveCustomProfileRequest`，response 为 `SuiteUserCommonResponse`。
- `Sekai.CustomProfile.PutUserUpdateProfileAPI.OnReceivecResponce`: 确认成功后合并 `response.updatedResources`。
- `Sekai.CustomProfile.CustomProfileUtility.SaveProfileAPI`: 自定义名片保存的通用封装。
- `Sekai.CustomProfile.CustomProfileUtility.ChangeProfileNameAPI`: 改名入口，负责名称校验并复用保存接口。
- `Sekai.CustomProfile.CustomProfileUtility.ReorderCardAPI`: 卡片重排入口，复用保存接口。
- `Sekai.CustomProfile.ScreenLayerCustomProfileSelect.OnChangeProfileName` / `ReorderCardIfNeedAsync`: 自定义名片选择页的改名和重排触发点。

## POST `/api/user/{userId}/custom-profile/{customProfileId}/custom-profile-card/{customProfileCardId}`

> 审计版本: jp-6.5.5
> 关键词：自定义名片，卡片，创建，缩略图

创建新的自定义名片卡。客户端保存卡片时会先检查当前名片下是否已有目标 `customProfileCardId`；若不存在，就用 POST 创建，并上传缩略图数据和卡片内容。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `customProfileId`: 自定义名片 ID。
- Path `customProfileCardId`: 名片卡 ID。
- Body `thumbnail`: 缩略图字符串。客户端由卡片截图编码生成。
- Body `customProfileCard`: `ProfileCardData`，自定义名片卡内容。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据。客户端成功后会合并到本地用户数据，通常包含更新后的 `userCustomProfileCards`，其中的 `thumbnailPath` 会被后续缩略图下载使用。

### 客户端请求时机

目前确认有这些时机：

1. 自定义名片卡编辑器保存新卡片时请求。
   - 客户端构建 `ProfileCardData`。
   - 截取卡片画面并编码为 `thumbnail`。
   - 当前名片下找不到该 `customProfileCardId` 时走创建接口。
   - 请求成功后合并 `updatedResources`，外层保存流程继续返回名片选择页或刷新显示。

2. 自定义名片选择页复制或创建卡片并保存时请求。
   - 选择页把目标贴图和卡片数据交给保存封装。
   - 保存封装判断目标卡片不存在后执行创建。

### 客户端切入点

- `Sekai.CustomProfile.PostUserCreateProfileCardAPI.Execute`: 确认 path 为 `user/{userId}/custom-profile/{customProfileId}/custom-profile-card/{customProfileCardId}`，method 为 POST，request 为 `UserSaveCustomProfileCardRequest`。
- `Sekai.CustomProfile.PostUserCreateProfileCardAPI.OnReceivecResponce`: 确认成功后合并 `response.updatedResources`。
- `Sekai.CustomProfile.CustomProfileUtility.SaveCardAPI`: 判断目标卡片是否已存在；不存在时走创建接口。
- `Sekai.CustomProfile.CustomProfileUtility.EncodeCardTexture`: 缩略图编码入口。
- `Sekai.CustomProfile.ScreenLayerCustomProfileCardEditor.SaveAndReturnTopAsync`: 卡片编辑器保存入口。
- `Sekai.CustomProfile.ScreenLayerCustomProfileSelect.OnSaveCard`: 选择页保存卡片入口。

## PUT `/api/user/{userId}/custom-profile/{customProfileId}/custom-profile-card/{customProfileCardId}`

> 审计版本: jp-6.5.5
> 关键词：自定义名片，卡片，更新，缩略图

更新已有自定义名片卡。客户端保存卡片时若当前名片下已存在目标 `customProfileCardId`，就用 PUT 覆盖保存缩略图数据和卡片内容。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `customProfileId`: 自定义名片 ID。
- Path `customProfileCardId`: 名片卡 ID。
- Body `thumbnail`: 缩略图字符串。客户端由卡片截图编码生成。
- Body `customProfileCard`: `ProfileCardData`，自定义名片卡内容。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据。客户端成功后会合并到本地用户数据，通常包含更新后的 `userCustomProfileCards` 和新的 `thumbnailPath`。

### 客户端请求时机

目前确认有这些时机：

1. 自定义名片编辑器保存已有卡片时请求。
   - 客户端构建最新 `ProfileCardData` 和缩略图。
   - 当前档案下能找到该 `customProfileCardId` 时走更新接口。
   - 请求成功后合并 `updatedResources`，并继续外层保存完成流程。

2. 自定义名片选择页覆盖已有卡片时请求。
   - 选择页把目标贴图和卡片数据交给保存封装。
   - 保存封装判断目标卡片已存在后执行更新。

### 客户端切入点

- `Sekai.CustomProfile.PutUserUpdateProfileCardAPI.Execute`: 确认 path 为 `user/{userId}/custom-profile/{customProfileId}/custom-profile-card/{customProfileCardId}`，method 为 PUT，request 为 `UserSaveCustomProfileCardRequest`。
- `Sekai.CustomProfile.PutUserUpdateProfileCardAPI.OnReceivecResponce`: 确认成功后合并 `response.updatedResources`。
- `Sekai.CustomProfile.CustomProfileUtility.SaveCardAPI`: 判断目标卡片是否已存在；存在时走更新接口。
- `Sekai.CustomProfile.CustomProfileUtility.EncodeCardTexture`: 缩略图编码入口。
- `Sekai.CustomProfile.ScreenLayerCustomProfileCardEditor.SaveAndReturnTopAsync`: 卡片编辑器保存入口。
- `Sekai.CustomProfile.ScreenLayerCustomProfileSelect.OnSaveCard`: 选择页保存卡片入口。

## DELETE `/api/user/{userId}/custom-profile/{customProfileId}/custom-profile-card`

> 审计版本: jp-6.5.5
> 关键词：自定义名片，卡片，删除，排序

删除自定义名片。客户端当前确认的 UI 流程是单卡删除；API 封装同时支持多个 `customProfileCardId` query，因此服务端应按重复 query 处理。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `customProfileId`: 自定义名片 ID。
- Query `customProfileCardId`: 要删除的名片卡 ID。多个 ID 时重复追加该 query。
- Body: 无请求体。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据。客户端成功后会合并到本地用户数据，用于刷新 `userCustomProfileCards` 和删除后的排序状态。

### 客户端请求时机

目前确认有这些时机：

1. 自定义名片选择页删除选中的卡片时请求。
   - 用户点击删除后会先进入确认流程。
   - 确认后调用删除封装。
   - 请求成功后合并 `updatedResources`，外层 UI 刷新卡片列表。

### 客户端切入点

- `Sekai.CustomProfile.DeleteUserProfileCardAPI.Execute`: 确认 path 为 `user/{userId}/custom-profile/{customProfileId}/custom-profile-card`，method 为 DELETE，query 为一个或多个 `customProfileCardId`。
- `Sekai.CustomProfile.DeleteUserProfileCardAPI.OnReceivecResponce`: 确认成功后合并 `response.updatedResources`。
- `Sekai.CustomProfile.CustomProfileUtility.DeleteCardAPI`: 单卡删除的通用封装。
- `Sekai.CustomProfile.ScreenLayerCustomProfileSelect.OnClickDelete`: 自定义名片选择页删除入口。
- `Sekai.CustomProfile.CustomProfileUtility.OpenCheckDeleteCardDialog`: 删除确认弹窗入口。

## GET `/image/custom-profile-card/thumbnail/{hash}/{thumbnailId}`

> 审计版本: jp-6.5.5
> 关键词：自定义名片，缩略图，图片，缓存

下载自定义名片卡缩略图。客户端从 `userCustomProfileCards.thumbnailPath` 拿到图片路径后，拼出完整图片 URL，使用普通图片请求下载并缓存为 PNG。

### 请求参数

- Path `hash`: 缩略图路径中的 hash 段。
- Path `thumbnailId`: 缩略图 ID。
- Body: 无请求体。

### 返回字段

- 图片二进制数据。客户端按纹理加载，成功后写入本地缩略图缓存。

### 客户端请求时机

目前确认有这些时机：

1. 自定义名片选择页显示卡片缩略图时请求。
   - 客户端先根据 `thumbnailPath` 查本地缓存。
   - 缓存不存在时拼接缩略图访问 URL，并发起图片 GET。
   - 下载成功后转换成纹理，回调设置到对应卡片 cell，并把 PNG 写入缓存。

2. 创建或更新自定义名片卡后，后续页面刷新缩略图时请求。
   - 保存接口返回的 `updatedResources` 会更新本地 `thumbnailPath`。
   - 之后列表或查看页需要显示缩略图时再触发图片下载。

### 客户端切入点

- `Sekai.CustomProfile.UserCustomProfileCard.thumbnailPath`: 缩略图路径字段来源。
- `Sekai.CustomProfile.CustomProfileUtility.BuildCardThumbnailAccessUrl`: 通过图片域名前缀和 `thumbnailPath` 拼接完整访问 URL。
- `Sekai.CustomProfile.CustomProfileUtility.GetCacheOrDownloadProfileThumbnailTexture`: 先读本地缓存，未命中时发起图片 GET 并缓存结果。
- `Sekai.CustomProfile.ScreenLayerCustomProfileSelect.OnDownloadThumbnailTextureCoroutine`: 自定义档案选择页缩略图下载入口。

## POST `/api/user/{userId}/report/{reportedUserId}/custom-profile/{customProfileId}`

> 审计版本: jp-6.5.5
> 关键词：自定义名片，举报，社区，原因

举报其他用户的自定义名片。客户端通过通用社区举报流程收集举报类型和当前位置，提交后只需要空响应来完成外层举报流程。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `reportedUserId`: 被举报用户 ID。
- Path `customProfileId`: 被举报的自定义名片 ID。
- Body `userReportReason`: 举报原因对象。
- Body `userReportReason.userReportReasonTypes`: 举报类型字符串数组。客户端枚举包含 `harassment_myself`、`harassment_others`、`harassment_character`、`obscene`、`cheat`、`other`。
- Body `userReportReason.userReportLocation`: 举报发生位置。客户端从当前界面上下文生成。

### 返回字段

空响应。客户端只关注请求是否成功，然后执行举报完成回调。

### 客户端请求时机

目前确认有这些时机：

1. 自定义档案查看或相关社区入口触发举报时请求。
   - 客户端打开通用社区举报弹窗，让用户选择举报类型。
   - 确认后客户端把举报类型转换成字符串数组，并填入当前位置。
   - 提交前会把被举报用户加入本地已举报缓存。
   - 请求成功后执行外层完成回调，通常继续显示举报完成提示。

### 客户端切入点

- `Sekai.PostCustomProfileCommunityReport.Execute`: 确认 path 为 `user/{userId}/report/{reportedUserId}/custom-profile/{customProfileId}`，method 为 POST，request 为 `PostCustomProfileCommunityReportRequest`，response 为 `EmptyResponse`。
- `Sekai.PostCustomProfileCommunityReport.OnCallBack`: 确认该接口只转发完成回调，不合并用户资源。
- `Sekai.CommunityReportUtility.ExecuteCustomProfileCommunityReport`: 构造 `UserReportReason`、记录已举报用户，并执行自定义名片举报 API。
- `Sekai.CommunityReportDialog.Setup` / `GetSelectCheckBoxIndexList`: 通用社区举报弹窗的举报类型选择入口。

## GET `/api/module-maintenance/{kind}`

> 审计版本: jp-6.5.5
> 关键词：功能维护，抽卡，多人，虚拟Live，MySekai

检查指定功能模块是否处于维护中。客户端在进入可维护功能前会先请求该接口，用返回结果决定是否继续进入目标界面或走维护提示流程。

### 请求参数

- Path `kind`: 功能模块类型。当前确认客户端会发送 `GACHA`、`MULTI_LIVE`、`VIRTUAL_LIVE`、`BILLING_SHOP`、`EVENT`、`MYSEKAI`、`MYSEKAI_ROOM`。
- Body: 无请求体。

### 返回字段

- `moduleMaintenanceType`: 被检查的功能模块类型。
- `isOngoing`: 是否正在维护；客户端用它决定是否放行后续流程。

### 客户端请求时机

客户端不只在抽卡入口请求这个接口，目前确认有这些时机：

1. 通用界面跳转流程中，请求目标界面属于可维护功能时请求。
   - 客户端会先禁用点击并请求维护状态。
   - 如果不在维护中，继续执行原本的界面进入回调。

2. 抽卡、活动、多人 Live、虚拟 Live、付费商店等功能入口前请求。
   - `MenuScreenType` 会被映射成对应的模块类型。
   - 抽卡入口对应 `GACHA`。

3. OutGame 启动和虚拟 Live 相关流程中请求。
   - 客户端会检查虚拟 Live 模块维护状态。
   - 回调后再决定是否开放或继续相关入口流程。

4. MySekai 入口和访问相关流程中请求。
   - 客户端会分别检查 MySekai 本体和房间相关模块。
   - 维护中时会中断后续进入流程。

### 客户端切入点

- `Sekai.GetModuleMaintenanceAPI.Execute`: 确认 path 为 `module-maintenance/{kind}`，method 为 GET，response 为 `ModuleMaintenanceResponse`。
- `Sekai.GetModuleMaintenanceAPI.OnCallBack`: 确认该接口只转发回调，不合并用户资源。
- `CP.API.APIUtility.ExecuteGetModuleMaintenance`: 确认 `MenuScreenType` 到模块类型字符串的映射，以及通用请求封装。
- `Sekai.ScreenManager.CheckModuleMaintenance`: 通用界面跳转前的维护检查入口。
- `Sekai.OutGameController.CreateScreenCore`: OutGame 阶段检查虚拟 Live 模块维护状态的入口。
- `Sekai.Mysekai.MysekaiUtility.ExecuteGetModuleMaintenance`: MySekai 相关模块维护检查入口。

## PUT `/api/user/{userId}/gacha/{gachaId}/gachaBehaviorId/{gachaBehaviorId}`

> 审计版本: jp-6.5.5
> 关键词：抽卡，卡牌，资源，天井，bonus

执行一次抽卡行为。客户端根据抽卡按钮、资源消耗方式、bonus 奖励选择和剩余抽取次数选择不同 query 变体；成功后合并返回的用户资源差异，并用完整抽卡结果驱动动画和结果页。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `gachaId`: 抽卡池 ID。
- Path `gachaBehaviorId`: 抽卡行为 ID。
- Query `isPriorityUsePaidJewel`: 是否优先使用付费水晶。
- Query `executeCount`: 指定执行次数；仅指定次数变体发送。
- Query `selectedCardId`: 选择卡牌 bonus 奖励时发送。
- Query `selectedItemId`: 选择道具 bonus 奖励时发送。
- Body: 无请求体。客户端内部的 `UserGachaRequest` 只用于生成 path 参数。

### 返回字段

- `consumedCosts`: 本次抽卡消耗的资源。
- `obtainPrizes`: 本次抽卡获得的卡牌或奖品列表。
- `obtainGachaCeilItems`: 获得的天井道具资源。
- `obtainGachaBonusItems`: 获得的抽卡 bonus 道具。
- `obtainGachaExtras`: 获得的额外奖励资源。
- `obtainGachaFreebies`: 获得的赠品。
- `userGacha`: 更新后的当前抽卡状态。
- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据。
- `obtainCharacterAllBonuses`: 角色全收集类 bonus 奖励。
- `obtainCharacterRepeatedBonuses`: 角色重复获得类 bonus 奖励。

### 客户端请求时机

目前确认有这些时机：

1. 抽卡页点击抽卡并完成资源使用确认后请求。
   - 客户端先根据当前抽卡池和按钮行为构造 `gachaId`、`gachaBehaviorId`。
   - 如果需要优先付费水晶，会带 `isPriorityUsePaidJewel`。
   - 如果存在抽取次数限制且需要按剩余次数执行，会走 `executeCount` 变体。
   - 请求成功后合并 `updatedResources`，再进入抽卡动画和结果展示。

2. 抽卡 bonus 奖励需要玩家选择卡牌或道具时请求。
   - 选择卡牌奖励时发送 `selectedCardId`。
   - 选择道具奖励时发送 `selectedItemId`。
   - 请求发出前会记录当前 bonus point，用于成功后的 UI 表现和差异计算。

3. 抽卡结果页继续抽卡或重抽时请求。
   - 结果页复用抽卡执行封装。
   - 成功后继续刷新结果页、bonus 计量、资源显示和后续动画流程。

### 客户端切入点

- `Sekai.PutUserGachaAPI.Execute`: 确认普通 path 为 `user/{userId}/gacha/{gachaId}/gachaBehaviorId/{gachaBehaviorId}?isPriorityUsePaidJewel={bool}`，method 为 PUT。
- `Sekai.PutUserGachaSpecifySpinCountAPI.Execute`: 确认指定次数变体追加 `executeCount` query。
- `Sekai.PutUserGachaSelectedCardAPI.GetRequestURL`: 确认选择卡牌 bonus 变体追加 `selectedCardId` query。
- `Sekai.PutUserGachaSelectedItemAPI.GetRequestURL`: 确认选择道具 bonus 变体追加 `selectedItemId` query。
- `Sekai.PutUserGachaAPI.OnCallBack` / `PutUserGachaSpecifySpinCountAPI.OnCallBack`: 确认成功后合并 `response.updatedResources`。
- `Sekai.GachaUtility.ExecuteSpinAPI`: 抽卡执行的统一封装，负责选择普通、指定次数、选卡或选道具 API。
- `Sekai.GachaUtility.ExecuteSpinSelectedCardAPI` / `ExecuteSpinSelectedItemAPI`: bonus 奖励选择后的抽卡执行入口。
- `Sekai.ScreenGacha.ExecuteSpinGacha` / `OnCallBackResponcePutUserGachaAPI`: 抽卡页发起请求和结果处理入口。
- `Sekai.ScreenLayerGachaResult.OnCallBackResponcePutUserGachaAPI`: 结果页继续抽卡或重抽后的结果处理入口。

## PUT `/api/user/{userId}/exchange/gacha-ceil-item`

> 审计版本: jp-6.5.5
> 关键词：抽卡，天井，兑换，资源

执行抽卡天井道具兑换。客户端在抽卡页头部或抽卡兑换页确认兑换后提交兑换配置，成功后合并用户资源差异，并展示获得资源、服装或饰品等结果。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `gachaCeilExchangeIds`: 兑换条目 ID 数组。
- Body `gachaCeilExchangeRequest`: 单次兑换请求详情。
- Body `gachaCeilExchangeRequest.gachaExchangeId`: 兑换 ID。
- Body `gachaCeilExchangeRequest.exchangeCount`: 兑换数量。
- Body `gachaCeilExchangeRequest.gachaCeilExchangeSubstituteCostId`: 替代消耗 ID。
- Body `gachaCeilExchangeRequest.substituteCostCount`: 替代消耗数量。

### 返回字段

- `obtainUserResources`: 本次兑换获得的资源列表。
- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据。

### 客户端请求时机

目前确认有这些时机：

1. 抽卡页头部的天井道具兑换流程中请求。
   - 用户点击兑换入口后，客户端打开兑换确认弹窗。
   - 确认后调用兑换服务提交请求。
   - 请求成功后合并 `updatedResources`，并刷新抽卡页头部的天井道具和资源显示。

2. 抽卡道具兑换页中选择兑换项目后请求。
   - 页面会先构建可兑换项目列表和确认弹窗数据。
   - 用户确认后调用兑换服务。
   - 请求成功后保存 `exchangeResponse`，展示兑换结果，并按获得内容继续展示服装或饰品获得弹窗。

### 客户端切入点

- `Sekai.PutUserGachaCeilExchangeAPI.Execute`: 确认 path 为 `user/{userId}/exchange/gacha-ceil-item`，method 为 PUT，request 为 `UserGachaCeilExchangeRequest`。
- `Sekai.PutUserGachaCeilExchangeAPI.OnCallBack`: 确认成功后合并 `response.updatedResources`。
- `Sekai.Service.GachaItemExchangeDataService.Exchange`: 兑换请求的服务层入口，负责执行 API 并等待完成状态。
- `Sekai.Service.GachaItemExchangeDataService.OnFinishExchangeAPI`: 确认服务层记录成功/失败状态和 HTTP 状态。
- `Sekai.GachaHeaderExtension.OnClickExchange` / `ExecuteExchange`: 抽卡页头部天井兑换入口。
- `Sekai.GachaItemExchangeHeaderExtension.ExecuteExchange`: 抽卡兑换页头部兑换入口。
- `Sekai.ScreenLayerGachaItemExchange.OnClickCell` / `OnClickDialogOK`: 抽卡兑换页选择项目并确认兑换的入口。
- `Sekai.ScreenLayerGachaItemExchange.OnFinishedExchangeResultDialog`: 兑换结果后续处理入口。

## PUT `/api/user/{userId}/rate-choice-gacha-wish`

> 审计版本: jp-6.5.5
> 关键词：抽卡，Rate Choice，愿望，卡牌选择

保存 Rate Choice 抽卡的愿望选择。客户端在卡牌选择页确认选择后提交当前选择列表，成功后合并用户资源差异并继续选择完成流程。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `gachaId`: Rate Choice 抽卡池 ID。
- Body `rateChoiceGachaDetails`: 选择详情列表。
- Body `rateChoiceGachaDetails[].rateChoiceGachaWishId`: 愿望槽位 ID。
- Body `rateChoiceGachaDetails[].gachaDetailId`: 被选择的抽卡详情 ID。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据。

### 客户端请求时机

目前确认有这些时机：

1. Rate Choice 抽卡卡牌选择页点击确定后请求。
   - 客户端先检查已选卡牌状态，并根据是否存在重复成员决定展示普通确认或重复确认弹窗。
   - 用户在确认弹窗中确定后，客户端构造 `UserRateChoiceGachaWishRequest`。
   - 请求成功后合并 `updatedResources`，再继续外层选择完成流程。

2. 卡牌选择页随机选择或手动选择后，最终仍通过同一确认流程请求。
   - 随机选择只改变本地选择列表。
   - 只有用户点击确定并通过确认弹窗后才提交 API。

### 客户端切入点

- `Sekai.Api.PutUserRateChoiceGachaWishApi.Execute`: 确认 path 为 `user/{userId}/rate-choice-gacha-wish`，method 为 PUT，request 为 `UserRateChoiceGachaWishRequest`。
- `Sekai.Api.PutUserRateChoiceGachaWishApi.OnCallBack`: 确认 API 类本身只转发回调。
- `Sekai.Service.PutUserRateChoiceGachaWishDataService.ExecuteAsync`: Rate Choice 愿望保存的服务层入口。
- `Sekai.Service.PutUserRateChoiceGachaWishDataService.OnFinishAPI`: 确认成功后保存 response 并合并 `response.updatedResources`。
- `Sekai.ScreenLayerRateChoiceGachaCardSelectPresenter.OnClickDecideButton`: 点击确定后的检查和确认弹窗入口。
- `Sekai.ScreenLayerRateChoiceGachaCardSelectPresenter.ShowRegisterConfirmDialog`: 注册确认弹窗入口。
- `Sekai.ScreenLayerRateChoiceGachaCardSelectPresenter.ExecutePutUserRateChoiceGachaWishAsync`: 确认后实际执行保存请求的入口。

## GET `/{gameVersion}/{appHash}`

> 审计版本: jp-6.5.5
> 关键词：版本，AppInfo，资源域名，启动

获取客户端启动用的 AppInfo。客户端用当前版本标识和 app hash 拼出完整 URL，请求成功后写入运行时环境配置，后续登录、服务器时间同步、资源域名和资源版本相关流程会依赖这份信息。

### 请求参数

- Path `gameVersion`: 客户端版本 API 标识，来自运行时环境配置。
- Path `appHash`: 客户端 app hash，来自运行时环境配置。
- Body: 无请求体。

### 返回字段

- `domain`: 后续游戏 API 或资源相关域名配置。
- `profile`: 环境标识，例如 `production`。
- `assetbundleHostHash`: 资源包 host hash；客户端保存到运行时环境配置。

### 客户端请求时机

客户端不只在标题页登录时请求这个接口，目前确认有这些时机：

1. Splash 启动流程中，签名 cookie 获取成功后请求。
   - 请求成功后客户端保存 AppInfo。
   - 随后继续请求服务器时间。
   - 请求失败时会重置服务器时间状态。

2. 标题页正常登录流程中，签名 cookie 获取成功后请求。
   - 请求成功后客户端加载本地账号并清理旧登录状态。
   - 如果本地账号存在，继续认证；如果不存在，进入注册新用户流程。

3. 标题菜单相关流程中，菜单签名 cookie 获取成功后请求。
   - 请求成功后继续标题菜单里的后续操作。
   - 请求失败时走登录错误处理。

### 客户端切入点

- `Sekai.GetAppInfoAPI.Execute`: 确认请求使用完整 URL，method 为 GET，response 为 `AppInfoResponse`。
- `Sekai.GetAppInfoAPI.OnCallBack`: 确认成功后调用 `EnvironmentConfig.SetAppInfo` 保存返回信息。
- `CP.API.APIUtility.ExecuteAppInfoAPI`: AppInfo 请求的通用封装，将请求状态转换成布尔回调。
- `Sekai.SplashController.OnFinishGetCookie` / `OnFinishAppInfoAPI`: Splash 启动阶段 AppInfo 请求和后续服务器时间同步入口。
- `Sekai.TitleController.OnFinishGetCookie` / `OnFinishAppInfoAPI`: 标题页登录阶段 AppInfo 请求和后续注册/认证入口。
- `Sekai.TitleController.OnFinishMenuSignedCookie`: 标题菜单相关流程中的 AppInfo 请求入口。

## GET `/api/user/{userId}/restrict-info`

> 审计版本: jp-6.5.5
> 关键词：账号继承，设备转移，限制，确认

获取当前账号的设备转移限制信息。客户端在设置账号继承、绑定平台账号等继承相关入口前会先请求它，用返回结果决定确认弹窗中是否追加限制提示。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body: 无请求体。

### 返回字段

- `isRestrictDeviceTransfer`: 是否处于设备转移限制中；客户端用它决定是否展示限制提示。
- `restrictEndAt`: 限制结束时间，毫秒时间戳。
- `restTransferCount`: 剩余可转移次数。
- `isWorldBloomChapter`: 当前限制是否与 World Bloom chapter 规则有关。
- `restrictRank`: 限制相关等级，可空。
- `gameCharacterId`: 限制相关角色 ID，可空。

### 客户端请求时机

目前确认有这些时机：

1. 账号继承设置页点击 ID/password 继承设置时请求。
   - 请求成功后客户端保存限制信息。
   - 随后打开密码输入弹窗；如果存在限制，后续确认或结果提示会追加限制说明。

2. 账号继承设置页点击平台账号绑定或继承设置时请求。
   - 客户端先拉取限制信息，再继续平台账号登录、绑定确认或继承确认流程。
   - 返回的限制信息会用于平台账号继承确认弹窗。

### 客户端切入点

- `Sekai.GetRestrictInfoAPI.Execute`: 确认 path 为 `user/{userId}/restrict-info`，method 为 GET，response 为 `UserRestrictInfo`。
- `Sekai.InheritTopDialog.GetRestrictInfo`: 继承入口统一的限制信息请求封装，请求成功后保存 `userRestrictInfo` 并执行后续回调。
- `Sekai.InheritTopDialog.OnClickInheritIdPass`: ID/password 设置入口，请求限制信息后打开密码输入弹窗。
- `Sekai.InheritTopDialog.ExecutePlatformInherit` / `SignInPlatformAccount`: 平台账号绑定或继承设置入口，请求限制信息后继续平台账号流程。

## PUT `/api/user/{userId}/inherit`

> 审计版本: jp-6.5.5
> 关键词：账号继承，ID密码，设置，凭证，引继码

设置 ID/password 引继码形式的账号继承信息。客户端提交用户输入的继承密码，服务端返回生成的 `inheritId` 和用户资源差异，客户端随后展示继承 ID 与密码给用户保存。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `password`: 用户输入的继承密码。

### 返回字段

- `userInherit.inheritId`: 服务端生成的继承 ID；客户端会在设置结果弹窗中展示。
- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据。

### 客户端请求时机

目前确认有这些时机：

1. 账号继承设置页选择 ID/password 后，用户输入密码并确认时请求。
   - 客户端进入该流程前会先请求 `GET /api/user/{userId}/restrict-info`。
   - 密码输入弹窗会校验输入非空，确认后执行设置请求。
   - 请求成功后客户端合并 `updatedResources`，并打开设置结果弹窗展示 `inheritId` 和密码。
   - 结果弹窗关闭后回到账号继承入口并刷新显示状态。

### 客户端切入点

- `Sekai.PutUserInheritAPI.PutUserInheritAPI`: 确认 request body 只写入 `password`。
- `Sekai.PutUserInheritAPI.Execute`: 确认 path 为 `user/{userId}/inherit`，method 为 PUT，request 为 `UserIPassInheritRequest`，response 为 `UserIPassInheritResponse`。
- `Sekai.PutUserInheritAPI.OnCallBack`: 确认成功后合并 `response.updatedResources`。
- `Sekai.InheritInputPasswardDialog.RegisterInherit`: 密码输入确认后的 API 执行入口。
- `Sekai.InheritInputPasswardDialog.OnFinishInheritPlatformPlan`: 设置成功后打开结果弹窗，并把 `inheritId` 和输入密码传入展示。

## POST `/api/inherit/user/{inheritId}`

> 审计版本: jp-6.5.5
> 关键词：账号继承，ID密码，预览，切换账号，引继码

使用 ID/password 引继码查询或执行账号继承。客户端第一次请求通常用于预览目标账号并打开确认弹窗；用户确认后会再次请求并执行继承，成功后用返回的 `credential` 切换本地账号。

### 请求参数

- Path `inheritId`: 用户输入的继承 ID。
- Query `isExecuteInherit`: 是否执行继承；客户端发送 `False` 做预览，发送 `True` 执行继承。
- Header `X-Inherit-Id-Verify-Token`: 继承校验 token，客户端用 `inheritId` 和 `password` 生成。
- Body: 无请求体。

### 返回字段

- `beforeUserGamedata`: 继承前账号的用户基础信息；响应模型包含该字段。
- `afterUserGamedata`: 继承目标账号的用户基础信息；客户端预览和执行成功后都会使用。
- `credential`: 执行继承成功后返回的新账号凭证；客户端用它创建或更新本地账号。
- `userEventDeviceTransferRestrict`: 设备转移限制信息；客户端用于继承确认和成功提示中的限制说明。

### 客户端请求时机

目前确认有这些时机：

1. 标题页账号继承流程中，用户输入 `inheritId` 和密码并确认后请求预览。
   - 客户端发送 `isExecuteInherit=False`。
   - 请求成功后用 `afterUserGamedata` 打开账号继承确认弹窗，展示即将继承的账号信息。
   - 若请求失败，客户端会把 `404`、`403` 等状态转换成继承专用错误提示。

2. 用户在继承确认弹窗中再次确认后请求执行。
   - 客户端发送 `isExecuteInherit=True`。
   - 请求成功且返回 `credential` 时，客户端用 `afterUserGamedata.userId` 和 `credential` 创建本地账号记录。
   - 随后清理部分本地缓存，重新加载账号信息，并展示继承成功提示。

### 客户端切入点

- `Sekai.PostUserInheritAPI.PostUserInheritAPI`: 构造参数为 `inheritId`、`password`、`isExecute`。
- `Sekai.PostUserInheritAPI.Execute`: 确认 path 为 `inherit/user/{inheritId}?isExecuteInherit={bool}`，method 为 POST，request 为 `EmptyRequest`，response 为 `PlatformInheritResponse`，并附加 `X-Inherit-Id-Verify-Token`。
- `Sekai.PostUserInheritAPI.CreateVerifyToken`: 确认校验 token 的数据来源为 `inheritId` 和 `password`。
- `Sekai.PostUserInheritAPI.OnCallBack`: 确认执行继承成功后会用返回的 `credential` 和 `afterUserGamedata.userId` 切换本地账号并清理本地缓存。
- `Sekai.InheritIPassExecuteDialog.ExecuteInherit`: 用户输入 ID/password 后的预览请求入口，发送 `isExecuteInherit=False`。
- `Sekai.InheritIPassExecuteDialog.OnFinishInheritPlan`: 预览成功后打开继承确认弹窗。
- `Sekai.InheritConfirmDialog.InheritIDPassSetup`: 确认弹窗接收 `inheritId`、`password`、目标账号信息和预览响应。
- `Sekai.InheritConfirmDialog.OnClickOK` / `OnFinishIPassInherit`: 用户最终确认后发送 `isExecuteInherit=True` 并处理成功提示。

## POST `/api/user/{userId}/live`

> 审计版本: jp-6.5.5
> 关键词：Live，开始，单人，技能，切入

开始一次普通单人 Live。客户端在最终确认页提交歌曲、难度、队伍、消耗 boost、是否 Auto 等信息，成功后拿到 `userLiveId` 和演出中需要的技能/切入数据，再进入实际 Live。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `musicId`: 曲目 ID；自制谱面流程中可能使用特殊值。
- Body `musicDifficultyId`: 难度 ID。
- Body `musicVocalId`: 歌唱版本 ID。
- Body `deckId`: 使用的队伍 ID。
- Body `boostCount`: 本次消耗的 boost 数。
- Body `isAuto`: 是否 Auto Live。
- Body `musicCategoryName`: 曲目分类名，例如普通曲或其他 live mode 对应分类。
- Body `customMusicScoreId`: 自制谱面 ID；普通曲可为空。

开局拒绝补充：已持有的限时曲在 `limitedTimeMusics` 期限外仍返回 400／`limited_time_music_out_of_term`。另一常驻曲的 `musicCategories` 仅有 `original`／`mv_2d`，传 `mv` 返回 404 空错误码，改为 `original` 后成功。两者错误响应均包含 `httpStatus`、`errorCode`、空 `errorMessage`。拒绝后的独立回读确认持有曲目、音源、成绩、任务、材料、Auto 次数及 boost 数量未变；自然恢复时间字段不作为不变性证明。

Server 已在创建会话前按 master 校验期限和分类，拒绝不修改用户状态，也不创建私有 Live 会话。Client 检查支持 `--replay-live-start-rejection`，两组官方错误响应与独立回读对拍通过，另检查本地完整用户状态及会话数量不变。常规成功开局与结算的 HTTP 检查继续通过。

时间规则来自 `MusicUtility.CheckLimitedTimeMusicExpiredAndShowDialog` 调用的 `TimeUtility.IsWithinTime`：包含开始、不含结束；结束为 0 且开始非 0 时仅检查开始，两者均为 0 时不开放。服务端沿用该规则，本地边界检查通过，但尚无官方恰好位于时间边界的样本。`ScreenLayerFreeLiveFinalConfirmation` 通过 `LiveUtility.GetMusicCategoryByLiveMode` 取分类并传入 `PostUserLiveAPI`。本轮未确认多个条件同时无效时的官方优先级、空分类、自定义谱面及其他开局限制；不得据此宣称开局校验完整。

难度选择补充：客户端正常流程从已选曲目的 `MusicDifficulties` 中查找难度 ID；自定义谱面分支使用 -1。但官方普通开局实际接受了曲目 361 搭配属于曲目 1 的难度 ID 1。该会话以零分、生命耗尽结算后，在曲目 361 下新增 Easy 的 `not_clear` 成绩；不能据客户端正常选曲过程推导服务端会拒绝跨曲难度。

另一次不存在的难度 ID 999999 也被开局接受，结算时返回空体 500，独立回读未发现成绩、任务、卡牌、材料、Auto 次数或 boost 数量变化。尚不能确认该错误是否稳定，也不能从 Suite 判断私有会话是否已移除。静态检查发现本地结算尚未拒绝不存在的难度，难度类型查询还会回退为 ID 字符串；该场景的结算对拍未完成，不为单次 500 编造稳定错误契约。

Client 的 `--replay-live-start` 已验证正常、跨曲难度、不存在难度三组开局受理、非空 Live ID、会话保存请求字段及选定用户状态的独立回读。技能随机顺序、切入列表和完整刷新响应不在通过范围，完整差异另行保留。跨曲难度失败结算的五类称号进度对拍通过；成绩数组顺序已按下述结算样本修复，其他完整响应差异仍保留，不能据此宣称全量一致。其他开局参数限制仍需各自证据。

音源持有补充：已持有的跨曲音源可以开局并完成结算；未持有的同曲替代音源与跨曲默认音源均返回 404，错误码、错误消息为空。购入跨曲默认音源对应的曲目后，保持开局请求完全相同，结果由 404 变为 200。Server 因此对普通音源按 `userMusicVocals.musicVocalId` 检查持有，不额外要求记录中的 `musicId` 等于请求曲目；季节音源另按下述规则处理。拒绝发生在创建会话之前，两组拒绝与两组受理均通过 HTTP 对拍及独立回读；购曲正常消耗材料，拒绝本身不改变持有、成绩或任务状态。

证据包括 `UserMusicVocal` 契约、`MasterMusicAllModel.MusicVocals` 和最终确认页选取音源的调用，以及购买前后完全相同的请求。客户端 `MusicUtility.IsOwnedMusicVocal` 按音源 ID 检查持有列表，但界面正常选择范围不代表服务端限制。此次仅验证普通手动 Live，Auto／自定义谱面的音源行为及多个无效条件的优先级仍需各自样本。

曲目持有补充：未购入的常驻曲目 11，使用其有效 Easy 难度、`image` 分类及早已持有的跨曲音源 1，开局返回 404，错误码和错误消息均为空。购曲后保持请求完全相同即返回 200，因此持有音源不能替代曲目解锁。Server 已按 `userMusics.musicId` 检查曲目持有；拒绝不创建会话、不改变用户状态。购买前后两组对拍和两组原有音源回归通过。

该结论来自 `UserMusic` 契约、master 的曲目／难度／分类对应关系与官方购曲前后对照。客户端 `MusicItemData.CheckReleasedMusic` 还检查当前音源持有，并包含季节音源放行分支；不能把常驻曲目的此次结果扩展为限时开放曲及自定义谱面的完整规则。

季节音源补充：`musicVocalType=april_fool_2022` 的音源在活动期外返回 409／`out_of_period_2022_april_fool`，错误消息为空。两个不同年度、均未持有的音源分别验证了该行为，优先于普通音源的未持有 404；独立回读确认持有、成绩、任务、材料、Auto 与 boost 数量未变。Server 已实现专用拒绝，Client 拒绝对拍支持 409；两组季节样本和普通音源的成功／拒绝回归均通过。

期间判断沿用逆向的 `SpecialSeasonUtility.GetCurrentSpecialSeason`／`IsAprilFool`：按 `specialSeasons.priority` 降序选择当前有效活动，再判断 `specialSeasonType`，时间区间沿用包含开始、不含结束的规则。客户端在活动期内允许选择季节音源，`MusicItemData.CheckReleasedMusic` 不要求该季节音源的永久持有记录。Server 已接入这一免持有分支，仍保留曲目持有检查，不授予永久音源。活动期间内、重叠活动优先级和时间边界仅通过逆向与本地检查验证，当前没有对应的官方正向样本；多条件同时无效的优先级亦未完整确认。

### 返回字段

- `updatedResources`: Live 开始后需要合并的局部资源；客户端模型中主要关注 break time 相关更新。
- `userLiveId`: 本次 Live 的唯一 ID；结算接口会作为 path 参数继续使用。
- `skills`: 本次 Live 中抽取的技能触发角色列表。
- `comboCutins`: 本次 Live 中组合切入配置。
- `isInBreakTime`: 是否进入 break time 状态；客户端用于后续流程判断。

### 客户端请求时机

目前确认有这些时机：

1. 单人 Live 最终确认页点击开始后请求。
   - 客户端从选曲、难度、歌唱版本、当前队伍、boost 设置、Auto 开关和 live mode 组装请求体。
   - 请求成功后保存 Live 开始所需数据，随后进入实际 Live 场景。
   - 请求失败时走 Live 开始专用错误处理，不进入 Live。

2. 自制谱面单人 Live 也复用该接口。
   - 客户端会把 `customMusicScoreId` 放入请求体。
   - 其余开始流程与普通单人 Live 一致。

### 客户端切入点

- `Sekai.PostUserLiveAPI.PostUserLiveAPI`: 确认 request body 字段来源：曲目、难度、队伍、boost、歌唱版本、Auto、分类和自制谱面 ID。
- `Sekai.PostUserLiveAPI.Execute`: 确认 path 为 `user/{userId}/live`，method 为 POST，request 为 `UserLiveRequest`，response 为 `UserLive`。
- `Sekai.PostUserLiveAPI.OnCallBack`: 确认 API 类本身只转发回调，不在该层合并完整用户资源。
- `Sekai.ScreenLayerFreeLiveFinalConfirmation.TransitionLive`: 单人 Live 最终确认页组装请求并执行开始 API 的入口。
- `Sekai.ScreenLayerFreeLiveFinalConfirmation.OnFinishLiveAPI`: Live 开始成功后的后续入口，用返回数据继续进入 Live。

## PUT `/api/user/{userId}/live/{userLiveId}`

> 审计版本: jp-6.5.5
> 关键词：Live，结算，奖励，经验，成绩

提交普通单人 Live 结算结果。客户端在 Live 结束进入结果页后提交分数、判定数、最大连击、生命值、镜像设置和已播放切入语音组，成功后合并用户资源并用响应驱动结果页奖励、经验、活动点和成就显示。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `userLiveId`: Live 开始接口返回的本次 Live ID。
- Body `score`: 本次分数。
- Body `perfectCount`: PERFECT 数。
- Body `greatCount`: GREAT 数。
- Body `goodCount`: GOOD 数。
- Body `badCount`: BAD 数。
- Body `missCount`: MISS 数。
- Body `maxCombo`: 最大连击数。
- Body `life`: 结束时生命值。
- Body `tapCount`: 谱面总 tap 计数。
- Body `musicCategoryName`: 曲目分类名。
- Body `isMirrored`: 是否镜像谱面。
- Body `ingameCutinCharacterArchiveVoiceGroupIds`: 本次 Live 中已播放且未读的切入角色档案语音组 ID 列表。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据。
- `score`、`perfectCount`、`greatCount`、`goodCount`、`badCount`、`missCount`、`maxCombo`: 服务端确认后的成绩数据。
- `highScoreFlg`: 是否刷新最高分。
- `fullComboFlg`: 是否达成 Full Combo。
- `fullPerfectFlg`: 是否达成 Full Perfect。
- `userExpResult`: 玩家等级经验变化。
- `deckCardExpResults`: 队伍卡牌经验变化。
- `unitExpResults`: 组合经验变化。
- `userDeck`: 更新后的队伍状态。
- `scoreRankRewards`、`playerRankRewards`、`limitedTermScoreRankRewards`: 成绩等级、玩家等级和限时成绩等级奖励。
- `userMusicAchievements`、`musicAchievementRewards`: 本次新达成的曲目成就及其奖励；没有新成就时均为空数组。
- `boost`: 本次使用的 boost 配置。
- `beforeEventPoint`、`afterEventPoint`: 活动点变化。
- `beforeEventItemQuantity`、`afterEventItemQuantity`: 活动道具数量变化。
- `beforeWorldBloomChapterPoint`、`afterWorldBloomChapterPoint`、`worldBloomChapterNo`: World Bloom chapter 相关结果。
- `bondsUpdateExpResults`: 羁绊经验变化和奖励。
- `userEventDeviceTransferRestrict`: 设备转移限制信息；结果页会用于限制提示。
- `userLivePoint`: Live mission 进度变化。
- `isEventMaintenance`: 活动是否维护中；客户端用于结果页维护提示。
- `isInBreakTime`: 是否进入 break time 状态。
- `customMusicScoreLiveResult`: 自制谱面结果信息。

玩家升级时，已核验 Rank 5→6、6→7 的响应会在完整 `updatedResources.userReleaseConditions` 中新增对应 `user_rank` 条件，旧记录及创建时间保留，新记录 `createdAt` 等于本次响应 `now`，不返回 `userId`。条件由 `releaseConditions.releaseConditionTypeLevel` 对应等级；客户端合并后据此判断设施入口是否解锁。Server 已在升级事务中补充该分支，Client 重放新增玩家等级解锁专项报告，两份样本的 HTTP、基线、响应及增量一致。其他类型条件仍需各自核验；尚未取得 Rank 30 解锁后的技能升级成功样本。

普通手动成绩顺序：官方新增较小曲目 ID 的成绩会插入已有曲目前；同曲响应和 Suite 回读为 `easy`、`expert`、`normal`，支持按难度字符串升序排列，而非按难度等级。Server 按 `musicId`、`musicDifficultyType` 排序，成绩未变且顺序未变时仍省略该刷新字段。逆向 `UserDataManager.UpdateMusicResults` 保存原始数组并建立曲目、难度索引；`GetUserMusicResult` 再匹配 `playType`。同曲、同难度的多种 `playType` 排列尚未核验，本次不增加其排序规则。

`--replay-live-result` 接受开局记录、结算记录、master 目录、输出目录，断言完整 `userMusicResults` 数组及顺序、`highScoreFlg`／`fullComboFlg`／`fullPerfectFlg`、状态增量和独立 Suite 回读。四组样本覆盖新增曲目、同曲新增难度、零分首次成绩及低分不刷新旧成绩；首次零分记录仍返回 `highScoreFlg=true`。修复前断言能检出顺序差异，修复后通过。这里只确认成绩部分，奖励、经验等完整响应差异仍另行保留。

成绩模式隔离：dump 的 `UserMusicResult` 定义 `solo`／`multi`；逆向 `MusicUtility.GetUserMusicResult` 把曲目、难度、模式传给 `UserDataManager.GetUserMusicResult`，后者在同曲同难度列表内逐项匹配 `playType`。Server 普通结算因此只查找或新增 `solo`，保留 `multi` 的高分、判定、MVP 和 Super Star 等字段。此前仅按曲目、难度取首条再改为 `solo`，会覆盖已有联机记录。小型夹具已覆盖只有联机成绩、联机记录排在单人成绩之前两种情况，以及编码失败整体回滚；两个已有官方单人成绩样本回归通过。现有抓包未找到 `multi` 成绩，仍需同曲同难度同时持有两种模式成绩的官方结算与回读，才能确认混合状态的官方行为和排列；本地混合检查不作为官方对拍证据。

Boost 契约补充：官方结算的 `boost` 包含整数 `bondsExpRate`，零体力手动样本为 1、消耗一体力的 Auto 样本为 5，与 `boosts` 对应行一致。原始 `MasterBoost` dump 及其 formatter 只有七个字段，未知 Key 走跳过分支；未发现客户端读取该新增字段的证据。构建副本现显式补充该 Key，Server 从 master 复制原值，不用其他倍率代替；原始 DLL 不变。`--replay-live-boost` 使用开局、结算、master、输出目录参数，断言完整 `boost` 对象，两组样本通过。协议检查确认新增字段的 JSON／MessagePack 往返及其余原始契约不变；这不代表羁绊经验发放已实现。

仍待确认：`userLivePoint.livePointBonusRemaining` 当前本地误用 `costBoost`；官方零体力样本为 3。逆向 `LiveReward.SetData` 将该字段传给 `LiveResultLiveMission.Initialize`，再由 `SetBonusRemaining` 填写剩余次数文本，但尚未确定服务端生成规则、每日重置与付费状态关系。需补充不同剩余次数及跨日样本，不能直接将本地逻辑替换为固定 3。

普通奖励重复采样：同曲 Normal、同音源、零体力、零分及生命耗尽的开局和结算请求完全相同，两次均获得 15 金币、2 个材料，但材料 ID 分别为 1、4。结算后快照和额外 Suite 回读确认材料到账；不能用单次观测固定资源箱序列。按 `resourceBoxes` 的 purpose、资源类型、ID、数量及倍率匹配，两个材料分别对应候选箱 18、21，金币对应 61；这只能证明资源内容匹配，尚不能证明选择算法、概率或适用的难度／评分范围。当前 Server 的两个历史组合仍是未完成实现，不扩展该硬编码。

普通评分按歌曲参考难度查表：逆向 `LiveMusicData` 将整首歌曲交给 `MusicUtility.GetPlayLevelScore`，后者按难度枚举升序取首个高于 Hard 的谱面，使用其 `playLevel` 查询 `playLevelScores.liveType=solo`。通常取 Expert，缺少时依次取 Master、Append；不是取实际游玩难度，也不是取最高等级。`ScoreLogic.UpdateScoreRank` 按包含边界的门槛比较。同一歌曲的各游玩难度共用此评分表，实际谱面等级、音符数及连击条件仍各自独立。缺参考谱面或评分表时不猜固定阈值。

曲目成就按完整 master 规则处理：`musicAchievements` 定义各评分及难度的连击比例，`resourceBoxes` 的 `music_achievement` 展开奖励。一次结算发放所有新跨过的门槛，按曲目和成就 ID 去重；成就奖励不乘评分掉落的 boost 倍率。高档位可能同时包含水晶和材料，不能从低档金币样本推断资源类型。小型夹具覆盖 C／B／A／S 门槛和前一分、直接到 S 跨多档、已领低档、连击向上取整、难度隔离、不同资源入账及重复领取；另以乱序难度和不同等级验证参考谱面选择、缺项拒绝。这些是规则检查，不替代各高档位的官方验证。

官方首次 Normal FC 样本返回四项本次成就及四笔金币奖励；重复 FC 返回两个空数组。此前 Server 已入账但漏返两个字段，现补入 dump 构建副本，并在结算编码时省略奖励的零值 ID／level。新增成就后的持久数组及刷新按曲目 ID、成就 ID 升序排列；本次成就数组只包含新记录。`--replay-live-achievement` 接受开局、结算、master、输出目录，断言分数、评分、两个响应数组、成就刷新、状态增量和独立 Suite 回读。首次及重复样本对拍通过；专项报告不覆盖随机掉落及经验。原始 dump 未定义这两个字段，不能据抓包宣称旧客户端会直接读取它们。

评分掉落与经验仍未完整泛化：目前未匹配两个历史组合的评分掉落返回空，玩家经验也只有部分 D／C 档分支，不能将其称为完整普通结算。早期一次模拟请求被官方以 `409 invalid_live` 拒绝，独立回读确认成绩、成就、资源及经验未变；该拒绝不证明奖励为空，也尚不能确定是哪项输入触发校验。后续仍需 A／S 档成功样本及奖励池选择、权重证据，不按每份新样本追加硬编码分支。

官方验证：歌曲 1/Easy 的 400000、400001、410000、430000 分均为 C，450000 分为 B。根因是歌曲参考难度为 Expert、等级 22，对应 solo B 门槛 434000，之前误用了 Easy 等级 5。相邻边界采样确认 433999 为 C、434000 为 B，均完成额外 Suite 回读。官方当前版与旧版评分表相同，不能把此前差异归因于版本或把比较符改为严格大于。

Client 的 `audit-live-rewards <capture.json> <resourceBoxes.json> <report.json>` 离线检查成功结算，列出评分奖励的单明细展开箱候选，并核对金币、材料、练习券和免费水晶的前后增量。收入包含评分、曲目成就、玩家升级和限时 `obtainedRewards`，成就与升级水晶不额外乘评分掉落倍率；同时检查付费余额，不能以免费／付费总额相同掩盖误发。响应外新增或少发资源也会报差异，缺少水晶余额即停止，遇到未覆盖资源类型返回未验证。报告不含账号信息，不能代替独立官方回读或完整奖励算法对拍。逆向 `ScreenLayerFreeLiveResult` 将响应 `scoreRankRewards` 直接交给 `LiveReward.Data` 展示；该调用未提供服务端抽取规则，仍需奖励选择表或足以区分规则的后续样本。

450000 分的官方样本在零体力下增加玩家经验 240，成就2发放20免费水晶，额外 Suite 确认到账；C 档样本增加经验 200。修复参考谱面查询后，400001、450000 及两份相邻边界样本的评分与成就专项对拍均通过，修复前多发成就和20水晶的差异记录保留。高档经验及随机掉落不在该专项对拍范围内，仍待补齐。

Client 测试入口 `--replay-live-experience <start> <clear> <master> <output>` 单独比较玩家与卡牌的经验、等级响应、刷新、持久变化及独立 Suite 回读；任一差异均使运行失败。零体力 C 档样本全部一致，B 档样本明确失败：官方玩家经验增加240、未满级卡牌增加720，本地均未增加。该入口不核验随机掉落或升级奖励。客户端 `LiveReward.SetData` 显示服务端经验结果的总经验差值，不能据此推导未观测档位的经验公式。

按参考谱面 A 门槛911400分采样返回 HTTP 409，未重试结算。独立 Suite 确认玩家与卡牌经验、材料、练习券、水晶、成绩、成就和 Live 任务状态未变化；体力仅恢复时间戳变化。拒绝原因尚未定位，此样本不能证明 A 档奖励内容。

### 客户端请求时机

目前确认有这些时机：

1. 单人 Live 结束进入结果页后请求。
   - 客户端从本地 Live 结果数据组装分数、判定数、最大连击、生命值和 tap 计数。
   - 客户端同时计算本次播放但未读的切入角色档案语音组 ID，并放入请求体。
   - 请求成功后合并 `updatedResources`，再继续结果页的成绩、经验、奖励、活动点、羁绊和 Live mission 展示。

2. 自制谱面单人 Live 结算也复用该接口。
   - 请求会带当前曲目分类和可能的自制谱面上下文。
   - 响应中的 `customMusicScoreLiveResult` 用于结果页显示自制谱面相关结果。

### 客户端切入点

- `Sekai.PutUserLiveClearAPI.PutUserLiveClearAPI`: 确认构造参数为 `userLiveId` 和 `UserLiveClearRequest`。
- `Sekai.PutUserLiveClearAPI.Execute`: 确认 path 为 `user/{userId}/live/{userLiveId}`，method 为 PUT，request 为 `UserLiveClearRequest`，response 为 `UserLiveClearResponse`。
- `Sekai.PutUserLiveClearAPI.OnCallBack`: 确认成功后合并 `response.updatedResources`。
- `Sekai.Result.ScreenLayerFreeLiveResult`: 普通单人 Live 结果页组装 `UserLiveClearRequest` 并执行结算 API 的入口。
- `Sekai.Result.ScreenLayerFreeLiveResult.OnFinishLiveClearAPI`: 结算成功后的结果页数据处理入口。
- `Sekai.ResultUtility.GetUnreadPlayedCutInVoiceGroupIds`: 确认 `ingameCutinCharacterArchiveVoiceGroupIds` 的来源是本次已播放且未读的切入语音组。

### 普通结算奖励的样本覆盖与缺口

现有场景记录中汇总了76份成功的普通结算响应，按响应的评分、体力消耗和经验倍率分组如下。数量表示记录数，不表示独立账号数；该汇总未按 Auto、失败条件、游戏版本和谱面进一步分层，也未替代逐份持久状态对拍。

| 评分 | costBoost | expRate | 玩家总经验增量 | 记录数 |
| --- | --- | --- | --- | --- |
| D | 0 | 1 | 20 | 8 |
| D | 3 | 15 | 300 | 1 |
| C | 0 | 1 | 200 | 27 |
| C | 1 | 5 | 1000 | 4 |
| C | 5 | 25 | 5000 | 9 |
| C | 10 | 30 | 6000 | 25 |
| B | 0 | 1 | 240 | 2 |

C档样本符合基础经验200乘 `boost.expRate`；两份B档样本增量240，但尚无B档其他倍率及A／S档成功样本，不能据此宣称全档经验规则已还原。当前Server的B档零经验与这些样本不一致。

76份响应的 `addDailyBonusProgress` 均为0，`livePointBonusRemaining` 均为3，覆盖0、1、3、5、10体力消耗。这反证当前Server将 `costBoost` 填入剩余次数的实现，但不能证明该字段应永远固定为3。客户端 `LiveReward.SetData` 将这两个字段直接传给 `LiveResultLiveMission.Initialize`，后者分别保存并展示；未在该路径从体力重新计算剩余次数。每日发放条件、次数扣减、跨日及付费状态仍需补证。

Client 检查入口 `--replay-live-point <start> <clear> <master> <output>` 通过实际本地 HTTP 结算比较完整 `userLivePoint`，校验成功状态与 HTTP 状态；缺少该对象或存在差异即失败。零体力官方样本确认仅剩余次数字段不同：官方3、本地0。该检查覆盖响应中的普通进度、每日加成、剩余次数及周期，不替代任务持久状态、独立回读和跨日验证。

每日加成适用范围已从[官方 FAQ](https://pjsekai.sega.jp/faq/index.html)确认（2026-10-09查阅）：多人 Live 或 Cheerful Live 通关时，当日前三次获得额外 Live P，次数每日凌晨4点重置。普通单人演出不属于该触发范围，因此上述单人样本无法验证次数扣减；不能为单人结算按游玩次数新增每日奖励。FAQ未在该条注明时区、具体加成量或网络存档字段，这些仍需匹配版本的配置及多人样本。

`UserMultiLiveClearResponse` 同样返回 `UserLivePoint`，是后续取证入口。其请求 `UserMultiLiveClearRequest` 包含五个玩家成绩槽、总分、Super Fever、断线玩家及私人房间设置，不能将单人结算请求直接换路由发送。当前Server与协议Client未接入完整多人房间和结算流程；下一步需取得测试账号私人房间的开局、成功结算、随后单人结算及跨日回读，验证共享次数。`colorfulPassV2s.livePointRate` 提供通行证倍率，但本次查阅未证明每日加成以购买通行证为前提。

私人房间前置链路：`MultiRoomMatchingOrganizer.PublishReserveRoomRequest` 调用 `MultiLiveRoomMatchingController.ReserveRoom`，先准备实时连接，再由 `MatchingRoom.Create` 序列化 `CreateMultiLivePrivateRoomData` 并发送命令3060。其字段为 `MultiLiveRuleType`、`RoomTTL`、`RoomProperty`；预留成功后，创建流程才以返回的房间标识调用 `PatchPrivateRoomNumberAPI`。实时认证由 `GetUserDiarkisAuth` 发起，响应 `UserDiarkisAuthResponse` 包含TCP／UDP连接信息、会话和密钥。协议Client的记录脱敏现已覆盖 `clientKey`、`sid`、`encryptionKey`、`encryptionIv`、`encryptionMacKey`，保留端口及业务字段且不修改原始认证对象；这不表示实时连接或建房已实现。命令封装、同步属性键、HTTP路由字符串映射和实际响应仍需继续核验。

歌曲1／Easy、零体力的C档样本中金币均为150、初级练习券均为2，材料组合不同；两份B档样本金币180、初级练习券2，材料组合也不同。其中一份B档响应将同一材料ID的数量2和4分成两条返回，奖励响应不能擅自按资源ID合并。当前固定掉落箱序列不覆盖这些结果；master奖励明细、倍率和评分门槛本身不足以确定池选择及抽取权重。

## POST `/api/user/{userId}/live-character-archive-voice/live-result`

> 审计版本: jp-6.5.5
> 关键词：Live，角色档案，语音，结果

领取或标记 Live 结果相关的角色档案语音。客户端提交语音组 ID、Live 类型和 `userLiveId`，成功后通过返回的资源差异更新角色档案语音持有/已读状态。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `liveResultCharacterArchiveVoiceGroupId`: 结果页角色档案语音组 ID。
- Body `liveType`: Live 类型字符串。
- Body `userLiveId`: 关联的 Live ID。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后用于更新角色档案语音相关用户状态。

### 客户端请求时机

目前确认有这些时机：

1. Live 结果页的 3D 角色语音流程中请求。
   - 客户端会检查结果页角色语音组是否已读。
   - 未读时，播放或处理该语音后提交语音组 ID、Live 类型和 `userLiveId`。
   - 请求成功后合并返回的用户资源差异。

2. 新版 API 封装确认同一路径和同一组请求字段。
   - 该封装接收外部传入的 `userId` 和 `UserLiveCharacterArchiveVoiceLiveResultRequest`。
   - 触发入口未完全确认；若需要精确 UI 时机，需要补一份包含该请求的抓包或具体操作路径。

### 客户端切入点

- `Sekai.Api.PostUserLiveCharacterArchiveVoiceLiveResultApi.Execute`: 确认 path 为 `user/{userId}/live-character-archive-voice/live-result`，method 为 POST，request 为 `UserLiveCharacterArchiveVoiceLiveResultRequest`，response 为 `UserLiveCharacterArchiveVoiceLiveResultResponse`。
- `Sekai.ApiData.UserLiveCharacterArchiveVoiceLiveResultRequest`: 确认 request body 字段为 `liveResultCharacterArchiveVoiceGroupId`、`liveType`、`userLiveId`。
- `Sekai.ApiData.UserLiveCharacterArchiveVoiceLiveResultResponse`: 确认 response 只包含 `updatedResources`。
- `Sekai.PostUserLiveResultCharacterVoiceAPI.Execute`: 确认结果页语音链路也会请求同一路径，并提交同等含义的请求字段。
- `Sekai.Result.ResultCharacter3D.ExecuteUserLiveResultCharacterVoiceAPI`: Live 结果页 3D 角色语音播放后的请求入口之一。

## PUT `/api/user/{userId}/topic/{topicId}`

> 审计版本: jp-6.5.5
> 关键词：Topic，已读，解锁提示，登录，首页

标记一个用户 topic 已读。客户端在登录后、功能解锁提示展示后、部分首页/Live/虚拟 Live 教程流程中，会把未读 topic 的 `topicId` 提交给服务端，成功后合并返回的用户资源差异。

### 请求参数

- Path `userId`: 当前用户 ID。
- Path `topicId`: 要标记已读的 topic ID。
- Body: 无参数。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据，主要用于更新 `userTopics` 的已读状态。

### 客户端请求时机

目前确认有这些时机：

1. 登录后处理未读 topic 时请求。
   - 客户端从本地用户数据中取未读 topic，筛选特定类型后逐个提交。
   - 请求成功后合并 `updatedResources`，避免同一 topic 继续作为未读提示出现。

2. 功能页或弹窗展示 topic 后请求。
   - 客户端通过通用工具把 `MasterTopic.id` 转成 `topicId` 并执行请求。
   - Live top、首页、教程和虚拟 Live 相关页面都存在这类通用调用。

3. 虚拟 Live 教程未读 topic 会批量处理。
   - 客户端遍历未读 topic，筛选 `virtual_live_tutorial` 类型后逐个提交。
   - 该流程用于清理虚拟 Live 教程相关的新手提示状态。

### 客户端切入点

- `Sekai.PutTopicAPI.Execute`: 确认 path 为 `user/{userId}/topic/{topicId}`，method 为 PUT，request 为 `EmptyRequest`，response 为 `SuiteUserCommonResponse`。
- `Sekai.PutTopicAPI.OnCallBack`: 确认请求成功后合并 `response.updatedResources`。
- `Sekai.TopicUtility.IsUnreadTopic`: 确认客户端会按 topic type 从未读 topic 中取待展示项。
- `Sekai.TopicUtility.ReadTopicAPI`: 确认展示后的 topic 会交给通用执行入口提交已读。
- `Sekai.UIUtility.ExecuteTopicAPI`: 通用 topic 已读提交入口，使用 `MasterTopic.id` 创建请求。
- `Sekai.TitleController.RefeshUnreadTopic` / `Sekai.TitleController.ExecuteTopicAPI`: 登录后处理未读 topic 的入口之一。
- `Sekai.UserDataManager.ExecuteTopicAPIOfAllUnreadVirtualLiveTutorial`: 虚拟 Live 教程未读 topic 批量提交入口。

## PUT `/api/user/{userId}/appeal`

> 审计版本: jp-6.5.5
> 关键词：Appeal，已读，抽卡，商店，MySekai

标记一组 appeal 已读。客户端会根据 `MasterAppeal` 的目标类型和读取条件筛选需要提交的 appeal ID，展示过对应引导、提示或促销内容后，把 ID 列表提交给服务端，成功后合并返回的用户资源差异。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `appealIds`: 要标记已读的 appeal ID 数组。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据，主要用于更新 `userViewableAppeal.appealIds`。

### 客户端请求时机

目前确认有这些时机：

1. 抽卡页面展示 appeal 后请求。
   - 客户端按 `gacha` 类型取得可展示的 appeal。
   - 展示后筛选读取条件为 transition 且类型为 daily once 或 once 的项目，并提交其 ID。

2. 付费商店页面展示 appeal 后请求。
   - 客户端按 `billing_shop` 类型取得可展示的 appeal。
   - 展示完成后提交对应 `appealIds`，成功后更新本地已读状态。

3. MySekai 入口或场景展示 appeal 后请求。
   - 客户端按 `mysekai` 类型取得可展示的 appeal。
   - 展示完成后通过同一个异步工具提交已读。

### 客户端切入点

- `Sekai.PutUserAppealAPI.PutUserAppealAPI`: 确认 request body 为 `UserAppealRequest`，字段为 `appealIds`；支持单个 ID 和 ID 数组两种构造。
- `Sekai.PutUserAppealAPI.Execute`: 确认 path 为 `user/{userId}/appeal`，method 为 PUT，request 为 `UserAppealRequest`，response 为 `SuiteUserCommonResponse`。
- `Sekai.PutUserAppealAPI.OnCallBack`: 确认请求成功后合并 `response.updatedResources`。
- `Sekai.AppealUtility.FilterForReadAPI`: 确认提交前会按 target type、read condition 和 appeal type 筛选 ID。
- `Sekai.AppealUtility.GetUserAppealRequestAsync`: appeal 已读提交的异步通用入口。
- `Sekai.ScreenLayerGachaSelect.ExecuteAppealReadAPI`: 抽卡页面 appeal 已读提交入口。
- `Sekai.CrystalShop.CrystalShopContent.ExecuteAppealReadAPI`: 付费商店页面 appeal 已读提交入口。
- `Sekai.Mysekai.SceneMysekai.ExecuteAppealReadAPI`: MySekai 场景 appeal 已读提交入口。

## POST `/api/user/{userId}/a0030f53-41e9-4b52-a8a4-993b807d5869`

> 审计版本: jp-6.5.5
> 关键词：UUID，参数上报，客户端状态，登录，Integrity，安全

上报客户端本地环境/检测状态。客户端会把一组本地检测结果编码成 `param` 字符串后提交，服务端只需要返回空响应；该接口本身不返回用户资源，也不驱动 UI 展示。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `param`: 客户端生成的字符串，内容来自 `CDCParam`，会把若干本地状态字段编码后放入 `UserParamRequest.param`。

### 返回字段

无业务字段。客户端使用 `EmptyResponse`，成功后只结束本次请求回调。

### 客户端请求时机

目前确认有这些时机：

1. 用户认证/登录流程完成后触发参数上报。
   - `UserAccountManager.SendParam` 会先检查本地 `paramCompleted` 标记。
   - 如果本次进程内还没有上报过，会调用 `ACUtility.SendStatus`。
   - 上报发出后客户端把 `paramCompleted` 置为 true，避免同一进程内重复提交。

2. 参数构造发生在请求发出前。
   - `ACUtility.GetStatus` 创建并执行 `CDCParam`，收集本地状态。
   - `PostUserParamAPI` 构造 `UserParamRequest`，把状态字典编码成 `param`。
   - 请求不依赖服务端返回资源；失败不会直接阻断普通 UI 流程。

### 客户端切入点

- `Sekai.PostUserParamAPI.Execute`: 确认 path 为 `user/{userId}/a0030f53-41e9-4b52-a8a4-993b807d5869`，method 为 POST，request 为 `UserParamRequest`，response 为 `EmptyResponse`。
- `Sekai.PostUserParamAPI.PostUserParamAPI`: 确认 `CDCParam` 会被编码成 `UserParamRequest.param`。
- `Sekai.UserParamRequest`: 确认 request body 只有 `param` 字段。
- `Sekai.ACUtility.GetStatus`: 确认本地状态由 `CDCParam` 生成。
- `Sekai.ACUtility.SendStatus`: 参数上报的通用发送入口。
- `Sekai.UserAccountManager.SendParam`: 登录后触发入口，并用 `paramCompleted` 做进程内防重复。

## POST `/api/user/{userId}/8989be20-0722-421a-9669-235865d30abe`

> 审计版本: jp-6.5.5
> 关键词：UUID，SNC，Nonce，Play Integrity，校验，安全

SNC/Play Integrity 校验链路的第一步：客户端向服务端请求校验参数。服务端返回 `UserSNCResponse.response`，其中包含 `param1` 和 `param2`；客户端随后用这些参数继续生成 Play Integrity token。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body: 无参数。

### 返回字段

- `response`: `UserSNCParam` 对象，客户端用于后续 Play Integrity token 生成流程。
- `response.param1`: 服务端下发的校验参数之一。
- `response.param2`: 服务端下发的校验参数之一。

### 客户端请求时机

目前确认有这些时机：

1. `UserAccountManager.ExecuteIntegrityAPI` 判断需要执行完整校验时触发。
   - 客户端读取当前时间、本地 SNC 缓存和用户注册时间。
   - 只有满足节流条件后才继续执行；不是每次登录都会请求。
   - 确认的节流窗口为 `86400000` 毫秒，即约 24 小时。

2. 生成 Play Integrity token 前先请求该接口。
   - `PlayIntegrityService.GetIntegrityAPITokenAsync` 会先进入 nonce/服务端参数获取流程。
   - `PlayIntegrityService.GetNonceAsync` 通过 `PostUserSncAPI` 发出该 POST 请求。
   - 请求成功后客户端从 `UserSNCResponse.response` 取参数，继续生成 JWS。

### 客户端切入点

- `Sekai.PostUserSncAPI.Execute`: 确认 path 为 `user/{userId}/8989be20-0722-421a-9669-235865d30abe`，method 为 POST，request 为 `EmptyRequest`，response 为 `UserSNCResponse`。
- `Sekai.UserSNCResponse`: 确认 response body 包含 `response` 字段，类型为 `UserSNCParam`。
- `Sekai.UserSNCParam`: 确认参数字段为 `param1` 和 `param2`。
- `PlayIntegrityService.GetNonceAsync`: 确认 Play Integrity 流程会先通过 `PostUserSncAPI` 获取服务端参数。
- `PlayIntegrityService.GetIntegrityAPITokenAsync`: 确认该 POST 请求位于生成 Play Integrity token 之前。
- `Sekai.UserAccountManager.ExecuteIntegrityAPI`: 确认触发条件、24 小时节流和后续 token 提交流程。

## PUT `/api/user/{userId}/8989be20-0722-421a-9669-235865d30abe`

> 审计版本: jp-6.5.5
> 关键词：UUID，SNC，JWS，Play Integrity，提交，安全

SNC/Play Integrity 校验链路的第二步：客户端拿到 Play Integrity JWS 后回传给服务端。该接口和上一条 POST 使用同一路径，但 method、request 和 response 不同。

### 请求参数

- Path `userId`: 当前用户 ID。
- Body `param1`: 空字符串；客户端构造 `PutUserSncAPI` 时会把该字段置空。
- Body `param2`: Play Integrity 返回的 JWS 字符串。

### 返回字段

无业务字段。客户端使用 `EmptyResponse`，成功后更新本地 SNC 执行时间。

### 客户端请求时机

目前确认有这些时机：

1. POST 获取参数并生成 JWS 后请求。
   - 客户端先通过 `POST /api/user/{userId}/8989be20-0722-421a-9669-235865d30abe` 获取服务端参数。
   - 随后调用 Play Integrity 流程生成 JWS。
   - 最后通过该 PUT 请求把 JWS 放入 `param2` 提交。

2. PUT 成功后记录本地 SNC 时间。
   - `UserAccountManager.ExecuteIntegrityAPI` 在流程完成后把本地 `LastSNCAt` 更新为当前时间。
   - 后续在约 24 小时窗口内不会重复执行完整 SNC 流程。

### 客户端切入点

- `Sekai.PutUserSncAPI.Execute`: 确认 path 为 `user/{userId}/8989be20-0722-421a-9669-235865d30abe`，method 为 PUT，request 为 `UserSNCParam`，response 为 `EmptyResponse`。
- `Sekai.PutUserSncAPI.PutUserSncAPI`: 确认构造请求时 `param1` 为空字符串，`param2` 为传入的 JWS。
- `Sekai.UserSNCParam`: 确认 request body 字段为 `param1` 和 `param2`。
- `PlayIntegrityService.PostIntegrityAPITokenAsync`: 确认 JWS 通过 `PutUserSncAPI` 提交。
- `Sekai.UserAccountManager.ExecuteIntegrityAPI`: 确认成功后更新 `LastSNCAt`，并由本地缓存控制下一次触发时间。

### 配对关系说明

这组 SNC 接口必须按顺序理解：

1. 客户端先 POST 同一路径，请求服务端下发 `UserSNCResponse.response.param1/param2`。
2. 客户端用返回参数进入 Play Integrity token 生成流程。
3. 客户端拿到 JWS 后 PUT 同一路径，提交 `UserSNCParam.param2`。
4. 完整流程完成后客户端记录 `LastSNCAt`，约 24 小时内不再重复执行。

因此服务端实现时不能只按路径判断业务；同一路径下 POST 和 PUT 是同一校验流程的前后两步，request/response 模型也完全不同。

## POST `/api/user/{userId}/challenge-live-character/{characterId}`

首次选择可参与挑战 Live 的角色。

- Path：`userId`、`characterId`；无 query、无 body。
- Response：`UserChallengeLiveCharacterResponse.updatedResources`，新增对应的 `userReleaseConditions` 和 `userOneTimeBehaviors`。释放条件只返回 `releaseConditionId`、`createdAt`；一次行为保留 `userId`、`oneTimeBehaviorType`。
- `ScreenLayerChallengeLiveFinalConfirm.ConnectAPI` 在首次行为不存在且 `IsOrReleaseCondition` 成立时发送请求，成功后合并资源。首次资格由 `oneTimeBehaviors` 的 `challenge_live_character_force_release` 关联释放条件决定；当前 master 为玩家等级 5。
- 成功样本新增所选角色 `challengeLiveCharacters.orReleaseConditionId`，记录当前创建时间和首次行为；不创建编队、成绩或挑战阶段。普通的队长使用次数条件是另一条解锁路径。
- 官方重复请求同一角色返回 409，重新认证回读后条件和挑战数据不变。本地按相同状态码拒绝；错误正文尚未核验。

首次成功样本的相关响应及状态差异已通过本地 HTTP 重放。其他角色、普通次数解锁与异常账号状态还需补充官方样本。

## PUT `/api/user/{userId}/challenge-live-solo-deck/{characterId}`

保存指定角色的挑战编队。客户端与官方首次保存样本已核验。

- Path：`userId`、`characterId`；无 query。
- Body：`UserChallengeLiveSoloDeck`，包括 `characterId` 和可空的卡牌 ID `leader`、`support1`～`support4`。
- Response：`userChallengeLiveSoloDeck` 返回保存结果；`updatedResources` 包含挑战参与状态、编队、成绩、阶段、高分奖励五组数组，以及已存在的出勤状态 `userChallengeLivePlayDay`。单卡样本省略空支援位；保存不会创建阶段或成绩。
- 客户端 `ScreenLayerChallengeLiveFinalConfirm` 检测编队变化，通过 `PutUserChallengeLiveSoloDeckAPI` 保存；成功回调合并资源差异。`ChallengeLiveFinalConfirmDeckView` 按 master 条件与角色等级锁定支援位。
- 首次角色解锁只新增对应释放条件与一次行为，不创建编队。官方样本中，未保存编队直接开局返回 404，补充保存后开局成功。不能把角色解锁等同于编队初始化。

重复保存样本中，已完成的挑战阶段、成绩、参与和出勤状态保持不变。官方参与状态字段为 `musicVocalId`、`isAuto`；原始 dump 使用 `musicVoiceId` 且缺少 `isAuto`。构建副本保留 CLR 字段名 `musicVoiceId`，将其网络 Key 调整为 `musicVocalId`，并补充布尔字段 `isAuto`。原始 DLL 不变；协议检查仅允许这两项已核验差异，其余成员仍与原始元数据逐项比较。

首次与重复保存的官方样本经本地 HTTP 重放后，挑战相关响应及状态增量一致。`isAuto=true` 已验证序列化往返，尚不代表自动挑战结算已获官方验证。

需补样本：多卡编队、支援位等级边界及无效编队的官方错误正文。本地校验不代表这些错误响应已与官方一致。

## POST `/api/user/{userId}/challenge-live/solo`

挑战 Live 开局。`PostUserChallengeLiveAPI.Execute` 发送请求，成功回调合并资源，确认页使用返回会话和技能数据进入演出。

- Path：`userId`；无 query。
- Body：`UserChallengeLiveStartRequest`，包含 `characterId`、`musicId`、`musicDifficultyId`、`musicVocalId`、可空卡牌 ID `leader`／`support1`～`support4`、`musicCategoryName`、`isAuto`。
- Response：`userChallengeLiveId` 为后续结算会话；`skills` 为技能顺序；`updatedResources` 更新挑战状态。
- 首次单卡样本新增参与状态：`liveStatus=start`、`playCount=0`、`isAuto=false`、`playStartAt`，省略 `playEndAt`；未新增成绩或阶段。返回两项技能，均为该领队卡，seq 为 1、2。双卡连续两次开局样本均返回领队、支援、领队的三项技能；更多卡数及顺序随机性仍待核验。
- `ChallengeLiveUtility.GetRemainingCount` 从 master 的参与额度减去所有角色状态的 `playCount` 合计，最小为零。额度不能按每个角色独立计算；日界线仍待官方样本。
- 未结算时对同一角色和编队再次开局的样本返回成功，生成新的 `userChallengeLiveId` 并更新 `playStartAt`；仅保留一条该角色参与状态，`playCount` 仍为 0，未改变编队、成绩、阶段或高分奖励。随后提交旧会话返回 HTTP 404，回读确认挑战状态及经验、材料、体力未变化；提交新会话成功。
- 官方样本：当天已完成一次挑战、`playCount` 合计为 1 时，再次开局返回 HTTP 409。重新认证回读后，参与状态、编队、高分奖励、成绩、阶段和出勤六组数据均未变化；错误正文与其他拒绝条件仍待核验。

Server 已接入非自动、已保存且与请求一致的单卡或双卡编队开局；重开替换该角色私有会话，未保存编队返回 404，次数耗尽返回 409。自动模式、三卡以上或请求编队与存档不一致暂返回本地 501，此状态码不代表官方规则。跨日次数重置和结算仍未接入。

Client 的 `challenge-restart.json` 在角色 1 已解锁、持有卡牌 1 且仍有挑战次数时保存编队并连续开局。`--replay-challenge-start` 支持官方成功样本的本地重放：先检查会话与参与状态对应，再将本次生成的会话 ID 对应比较；业务时间和其他字段保留检查。单卡、双卡及重开样本的相关响应与状态增量一致，不覆盖背景资源。

## PUT `/api/user/{userId}/challenge-live/solo/{userChallengeLiveId}`

挑战 Live 结算。`PutUserChallengeLiveAPI` 提交结果并合并 `updatedResources`，结果页展示评分、经验、挑战阶段、高分及出勤奖励。

- Path：`userId`、开局返回的 `userChallengeLiveId`；无 query。
- Body：`score`、各判定计数、`maxCombo`、`life`、`tapCount`、`continueCount`、`musicCategoryName`、`isMirrored`。模型中的 fast／late／flick 计数标记为 IgnoreMember，不发送。
- 主要响应：`scoreRank`、成绩标志、玩家／卡牌经验、`userChallengeLiveStageResult`、`userChallengeLiveHighScoreResult`、各类奖励和出勤状态。成功样本的参与状态更新为 `cleared`、`playCount=1`，新增 `playEndAt`。
- 评分使用 `playLevelScores.liveType=challenge_live`，不能复用普通 solo 阈值。
- 高分奖励预览从当前角色的 `challengeLiveHighScoreRewards` 读取门槛，按 `highScore` 升序展示；列表的已领标记通过用户记录的 `challengeLiveHighScoreRewardId` 匹配 master `id`。结算响应 `userChallengeLiveHighScoreResult.rewards` 中对应字段名为 `challengeLiveHighScoreId`，资源列表为 `userResources`，不能与存档字段混用。角色 21 首次达到 100000 分的官方样本返回奖励条目 421、水晶 100，用户记录的 `challengeLiveHighScoreStatus=complete`；资源盒用途为 `challenge_live_high_score`。
- `ChallengeLiveUtility` 按各阶段所需点数累加和定位当前阶段，达到门槛即进入下一阶段。普通阶段样本验证了跨级和剩余点数；EX 阶段仍需独立核验。
- 两次 C 档样本的 `userLivePoint.addNormalProgress=30`、`addDailyBonusProgress=0`，与 `configs.obtain_live_point_for_challenge_live` 一致；`livePointBonusRemaining=3`。其中一份样本的 `userLiveMissions.progress` 从 150 增至 180，`paidProgress` 仍为 0。两份样本的 `userBoost` 均未变化；其他评分、付费状态和跨日情形仍待核验。

当前结算样本使用模拟输入。四次 C 档样本中，7999 分获得 200 点，8000 与 10000 分获得 201 点，100000 分获得 212 点；玩家经验均增加 4000。双卡样本的两张卡各增加 12000 经验，响应 index 为 1、2，与领队、支援位对应。部分评分奖励材料 ID 不同，发放规则仍待核验。其他评分经验、奖励抽取、跨日和自动挑战尚未核验。

新增 3999 分成功样本为 D 档：挑战点 200，玩家经验增加 400，两张卡各增加 1200 经验；评分奖励仅金币 10000、材料 1×40。首次出勤仍给水晶 20，Live 任务仍增加 30，样本活动积分及道具分别增加 12000、1200；限时生日奖励仍为材料 297×3。不得将此样本的材料属性固定为通用奖励。该样本的评分、阶段与角色升级、高分记录、首次出勤业务回放均通过，完整 HTTP 结算尚未接通。

生日限时奖励已接入业务层：材料取 `birthdayParties.deliveryItemMaterialId`，数量取 `configs.challenge_live_limited_term_score_rank_reward_rate`，通过资源服务发放；响应类型为 `birthday`。当前五份 D／C 档样本的 `limitedTermScoreRankRewards` 和对应材料库存一致。活动时间参考 `MysekaiBirthdayPartyUtility.IsWithinBirthdayTime`（RVA `0x571b310`）与 `TimeUtility.IsWithinTime`（RVA `0x4df8d20`），使用 `startAt <= timestamp < closedAt`，不使用 `birthdayStartAt` 作为掉落起点。此为客户端活动判断与当前样本的交叉验证，发奖起止边界未获官方实测；重叠生日活动、跨活动结算、自动、失败、会员及其他限时奖励仍未核验。无活动及时间边界只有本地检查，不代表完整结算接口已接通。

Server 已接入手动成功 D／C 档的玩家与卡牌经验业务，复用普通 Live 的玩家升级处理和卡牌经验处理；自动挑战、失败结算、会员及其他评分仍待核验。Client 测试入口 `--replay-challenge-exp <record> <master> <output>` 比较经验响应、玩家经验持久状态和卡牌状态，D 档双卡及 C 档单／双卡样本通过。回放另检查后续卡牌发放失败时回滚玩家和领队经验。本组样本没有玩家升级，不据此宣称挑战升级奖励或体力联动已获官方核验；完整结算路由仍未接入。

挑战活动积分业务已接入 marathon 活动：`(100 + floor(score / 20000)) × configs.challenge_live_event_point_rate`，道具数量为新增积分除以 `configs.event_item_reduction` 后向下取整。公式来自[公开分析](https://note.com/notnishikori_18/n/nd3b1719c555f)并经五份官方样本交叉验证：低于 20000 分增加 12000 积分／1200 道具，100000 分增加 12600／1260。活动道具按 `eventItems.eventId` 映射，通过资源服务发放；跨过 `releaseConditionType=event_point` 的 `releaseConditionTypeQuantity` 时记录解锁条件和结算时间，保留已有条件。

活动有效期采用 `startAt <= timestamp < aggregateAt`，依据 `EventUtility.IsPlayableEvent`（RVA `0x4d91984`）。开局与结算须属于同一活动；仅支持 marathon 且不统计队长活动次数的分支。自动、失败、跨活动、休息点数达到 master 上限及超过活动分数上限暂不支持。休息上限判断来自 `EventBreakTimeUtility.IsEventBreakPointOverMaxValue`，不将任意正点数视作休息状态；休息点数随时间恢复和休息中的奖励仍待核验。

Client 阶段回放已加入活动前后积分、道具数量及 `userEvents`、`userEventItems`、`userReleaseConditions` 的状态比较，五份样本一致。回放使用样本的真实编队，将玩家／卡牌经验与阶段、角色升级、任务、生日奖励、活动奖励及完成状态放在同一次用户操作中，并比较经验响应和卡牌持久状态。时间边界、首次创建活动记录和编码失败回滚另有本地检查；World Link、其他活动类型、排行榜及完整 HTTP 结算仍未接入。

普通阶段及角色升级业务已实现，完整结算路由尚未接入。已完成阶段保留为 `complete`，其 `point` 为该阶段门槛；当前阶段为 `in_progress`，保存剩余点数。逐阶段按 `challenge_live_stage` 用途读取资源盒，保留各份奖励，并累计 `completeStageCharacterExp`。角色等级按 `levels.levelType=character` 的累计门槛计算，更新 `userCharacters` 的等级、总经验和余经验；跨越的等级按 `characterRanks.rewardResourceBoxIds` 与 `character_rank_reward` 用途发奖。EX、角色满级及含额外解锁奖励的等级暂不支持。

挑战评分与普通演出共用歌曲参考难度规则：`ChallengeLiveBootData` 向 `LiveBootDataBase` 传入 `LiveType.challenge_live`，后者构造 `LiveMusicData`，最终由 `MusicUtility.GetPlayLevelScore` 选择同曲难度枚举升序首个高于 Hard 的谱面，以其 `playLevel` 查询 `playLevelScores.liveType=challenge_live`。Server 已共用这段查询，从 S 到 C 比较包含边界的门槛，低于 C 返回 D；缺参考谱面或表项时拒绝猜测，也不回退 solo 表。实际游玩难度不改变评分表。小型夹具用不同的游玩／参考等级及 solo／challenge 门槛验证所有档位边界，既有官方 D／C 档阶段业务回放通过。该查询尚未接入完整挑战结算路由。

新增官方区分样本：歌曲1/Easy 的4339分返回D，玩家经验增加400，额外Suite确认到账；歌曲参考Expert22的挑战C门槛为4340，旧查询误用Easy5会判C。新样本还确认已领奖的挑战新手任务继续计数，现已修复。好友任务在写前快照为726、结算响应为728、后快照为730；共用刷新及回放顺序修正后，评分、阶段、经验、任务响应及持久状态等全部专项比较一致。修复前失败报告保留；该业务回放仍不代表完整HTTP结算或随机掉落已实现。

普通挑战点数按 `challenge_base_point + floor(score / challenge_point_calc_value)` 计算；当前 master 参数为 200、8000。[公开分析](https://note.com/notnishikori_18/n/nd3b1719c555f) 明确给出同一公式，五份官方样本交叉验证了 3999、7999、8000、10000、100000 分的结果。Server 已将其接入手动成功、无会员的阶段业务；会员倍率、自动和失败结算仍待核验。

挑战任务联动已接入业务层：按 `configs.obtain_live_point_for_challenge_live` 增加免费 Live 任务进度，并按 `beginnerMissionV2Type=challenge_live_clear` 推进新手任务。五份官方样本中该新手任务从无记录变为进度 1、`isNewAchieved=false`，对应状态为 `achieved`，普通 Live 新手任务不变。任务周期按结算时间查询；自动、失败、会员、付费任务及无匹配周期暂不支持。完整结算入口、跨期演出及每日加成响应仍待接入和核验。

Client 测试入口 `--replay-challenge-stage <record> <master> <output>` 从请求分数独立计算评分和点数，再重放阶段业务，不使用官方 `addPoint` 作为输入。五份样本的评分、点数、阶段记录、角色状态及解码后的完整 `userChallengeLiveStageResult` 一致，涵盖角色 1 和 21 从等级 1 升至 3 的经验与奖励；该检查不覆盖网络字段省略规则或完整 HTTP 结算。样本新增的释放条件来自活动积分，不属于角色升级。

该回放同时比较 `userLiveMissions`、`userBeginnerMissionV2s`、`userMissionStatuses` 的完整业务状态，早期五份首次样本一致。回放时间固定为官方结算响应的 `updatedResources.now`，周期独立查询 master；用户 ID 映射为本地测试账号，不据此验证网络字段省略规则。后续官方样本确认挑战新手任务在 `received` 状态下仍由1增至2，状态保持已领取、`isNewAchieved=false`；额外Suite及后续只读请求保持此进度。Server 按 master 类型复用持续计数逻辑，达成及领奖只影响状态，不截断计数；不将此规则套用到已有封顶证据的普通Live任务。检查覆盖达成前后、领奖后计数及编码失败回滚，不重复达成或发奖。

扩大比较范围后，旧的首次D／C挑战样本暴露了响应提示缺口：官方在当次响应将新达成任务的 `isNewAchieved` 置为true，后续Suite为false；原有持久状态检查未覆盖这个差别。现由共用 `BeginnerMissionResponse` 映射：比较操作前后的新手达成状态，为所有本次新达成项生成提示，复制响应数组，不修改存档。练习、普通演出、MV、服装、商店和剧情控制器复用同一组件，原有窄响应及剧情类型限制保留。首次D、首次C和重复挑战的任务响应、后续Suite及其余业务比较项均一致；挑战仍为业务与响应组件回放，完整clear HTTP路由尚未接入。修复前失败报告保留。

成功挑战的完成状态已接入业务层：核对私有会话与参与状态后，将 `liveStatus` 从 `start` 改为 `cleared`、`playCount` 从 0 改为 1，并记录 `playEndAt`，保留开局时间和曲目字段。对应角色的 `play_live` 任务进度增加 1，首次新增时按任务类型、角色 ID 排序；`userCharacterLiveUsageCounts` 不增加队长或成员次数。五份官方样本的完成状态、角色任务及使用次数回放一致，覆盖首次新增和已有进度递增；角色任务跨门槛仍待官方样本。

完成后移除本地私有会话，同一会话不能再次完成。该防重复策略及编码失败时恢复会话、次数和角色任务有本地检查，不代表官方重复成功结算的状态码已核验；完整结算入口仍未接入，发奖与会话完成必须放在同一次用户操作中。

高分业务已实现：保存角色最高分，按 master 门槛发放未记录的奖励，保存 `complete` 记录。四份样本经 `--replay-challenge-high-score` 重放，覆盖未达门槛和首次达到单个门槛，解码后响应、最高分及已领记录一致。一次跨越多门槛、较低成绩和重复成绩只有本地检查，仍需官方样本。

首次出勤业务已实现：写前没有 `userChallengeLivePlayDay` 时，记录 `playDays=1`、状态 `received` 和开局时间，按 `challenge_live_play_day_reward` 资源盒发奖。客户端奖励期筛选为 `startAt < 时间 < endAt`，按 `priority` 升序取首项；使用 UTC+9、master 的 `date_change_hour` 和周一重置规则计算 `playDaysResetAt`。四份首次样本经 `--replay-challenge-play-day` 业务重放一致。后续出勤、跨日切／奖励期结算、其他周重置配置和选择奖励暂不支持，尚未据此接通完整结算路由。

## POST `/api/user/{userId}/challenge-live/receive-select-reward/{resourceId}`

领取挑战 Live 的可选出勤奖励。以下来自客户端静态审计，尚无官方成功领奖样本。

### 请求参数

- Path `userId`：当前用户 ID。
- Path `resourceId`：选中的 `MasterChallengeLivePlayDayReward.id`。虽然 API 参数名为 resourceId，实际不是 `resourceBoxId`，也不是盒内道具 ID。
- 无 query 和 body。

### 返回字段与处理

- `updatedResources`：API 成功回调合并到用户状态。
- `obtainRewards`：`UserResource[]`，上层用于显示领取结果。

### 客户端请求时机与证据

1. `DialogUtility.ShowChallengeLivePlayDayRewardDialogIfNeeded` 检查挑战出勤状态，按 `lastPlayStartAt` 查适用奖励期并打开选择弹窗。
2. `ChallengeLivePlayDayRewardSelectDialog.OnSelectItem` 用资源盒展示奖励，确认后回传选中的完整奖励条目。
3. 上层调用 `ChallengeLiveDataService.SelectReward(result.id)`；该服务将 ID 原样交给 `PostUserChallengeLiveReceiveSelectRewardAPI`，由其发送 POST。
4. 成功后合并资源差异并展示 `obtainRewards`。

待补材料：一次具备选择资格的成功请求、响应及前后用户状态，用于核验领取状态变化、资源增量和重复领取行为。不能仅凭 master 的奖励配置推断这些状态变化。

## PUT `/api/user/{userId}/material-exchange/{materialExchangeId}`

- Path：当前 `userId`、master 兑换项 `materialExchangeId`。
- Query：`costGroupId` 为消耗组，`count` 为兑换次数；不发送 body。客户端构造函数将次数限制为至少 1。
- Response：`updatedResources`、`releasedActionSetIds`。成功回调合并资源；兑换页据此更新库存及兑换状态。
- 证据：`PutUserMaterialExchangeAPI.Execute` 使用 PUT，按用户、兑换项、消耗组和次数拼接请求；`OnCallBack` 合并 `UserExchangeResponse.updatedResources`。
- 官方样本：兑换项 2、成本组 1、次数 2，扣除材料 15 共 200，增加中级练习券 30；兑换记录变为 `exchangeCount=2`、`totalExchangeCount=2`、`exchangeStatus=exchangeable`，并记录 `lastExchangedAt`。数值均与 master 消耗组及 `material_exchange` 资源盒一致。
- 再兑换一次后，两种次数均从 2 增至 3，更新时间改变，状态仍为 `exchangeable`。材料不足时官方返回 HTTP 409；重新认证回读确认材料、练习券、兑换记录不变，错误正文尚未对齐。
- 无上限样本省略 `exchangeRemaining`；未发生的 `lastExchangedAt`、`refreshedAt` 也省略，不发送零值。

Client 已支持 `material-exchange`、示例场景及 `--replay-material-exchange`，要求明确提供整数消耗组与正整数次数。Server 已接入 normal 商店中无刷新、无限量、无关联或附加奖励的材料换练习券分支；成本和奖励按 master 倍乘，次数累计、材料不足无副作用。未核验分支返回本地 501，不代表官方状态码。首次及再次兑换的相关 HTTP、基线、响应与增量对拍一致；周期刷新、限量、关联兑换及其他奖励仍待核验。示例兑换项须按当前 master 和账号库存确认。

## POST `/api/user/{userId}/boost-item`

- Path：当前 `userId`；无 query。
- Body：`UserBoostItemRequest.costs` 数组，每项含 `resourceId`、`resourceType=boost_item`、`quantity`，没有 `resourceLevel`。
- Response：`SuiteUserCommonResponse.updatedResources`；客户端合并道具库存和体力，随后刷新恢复弹窗。
- 请求时机：在体力恢复弹窗选定道具数量并确认。证据为 `BoostRecoveryDialog.OnClickItemRecoverOK`、`PostUserBoostItemAPI.Execute/OnCallBack` 及官方请求响应。
- 恢复量来自 `boostItems.recoveryValue`。已核验小道具 1 个恢复 1 点：15→16 时保留 `recoveryAt`，24→25 时将其设为响应 `now`；库存耗尽仍保留数量为 0 的条目。
- 已到自然上限仍可使用：官方 25→26 正常扣除一个道具，`recoveryAt` 更新为响应时间；自然上限不截断道具恢复。该样本的恢复响应重放一致，但固定重放时间导致满体力的写前查询时间与官方不同，基线及时间增量差异保留。

Server 已实现上述普通道具分支，成本通过资源服务扣除，库存和体力在同一事务提交。Client 支持 `boost-item` 及 `--replay-boost-item` 重放。两份官方样本的 HTTP、相关响应和状态增量一致；基线另有会员字段零值省略差异。满体力后状态回读更新时间不同，专项比较只在后续回读中排除该时间，恢复响应中的时间仍精确比较，完整差异另存。

普通账号的自然恢复已接入完整状态查询、普通 Live 结算及道具恢复。master 指定每 1800 秒恢复 1 点，上限 25；官方样本确认 16→17 时 `recoveryAt` 推进一个周期，剩余计时保留，连续查询不重复增加。Client 支持 `--replay-natural-boost`，该样本的体力及时间字段一致。另有普通 Live 样本确认 20→15 的扣除保留原恢复时间，本地重放一致。

普通 Live 升级样本补充：Rank 7→8，原体力 17，消耗 5、升级补充 10，最终为 22，`recoveryAt` 保持不变。Server 先扣除再补充升级体力，最终未到自然上限时保留计时；该样本与旧 Rank 6→7 样本的体力专项重放一致。Client 的 Live 重放新增 `boost-compare.json`，保留数量和时间的精确比较。

一次跨多个周期、自然恢复恰好满额及其他业务入口仍待核验。会员上限、硬上限溢出暂返回本地 501，不代表官方错误码；还需多道具组合、库存不足的官方样本。宝石恢复尚未实现。示例需要账号已持有对应道具。

## POST `/api/user/{userId}/card/{cardId}/material` 与 `/skill-practice-ticket`

- Path：当前 `userId`、持有的 `cardId`；无 query。
- Body：`costs` 数组，包含 `resourceType`、`resourceId`、`resourceLevel`、`quantity`。材料类型为 `material`，技能券为 `skill_practice_ticket`。客户端提交时将 `resourceLevel` 设为 0。
- Response：`updateExpResult` 用于展示升级前后经验，`updatedResources` 合并用户状态。
- 请求时机：玩家在技能升级选择页确认投入；证据为 `ScreenLayerSkillPracticeItemSelect.OnExecutePractice` 及两个 `PostUserCardSkillPractice*API.Execute`、`OnCallBack`。
- 入口解锁：`ScreenLayerPracticeCardSelect.SetupSkillPracticeTab` 经 `FacilityUtility.CheckReleased` 检查设施解锁。`facilities` 中 `skill_practice` 引用条件 10030，`releaseConditions` 对应玩家 Rank 30；客户端通过 `userReleaseConditions` 中的解锁记录判断入口是否可用。
- master：材料经验来自 `cardSkillCosts`，技能券经验来自 `skillPracticeTickets`；等级阈值按卡牌稀有度查 `levels`，上限查 `cardRarities`。

材料升级拒绝样本包含玩家 Rank 1 和 Rank 5 的账号，均未持有条件 10030 的解锁记录，低于技能升级入口要求。官方返回 HTTP 409，回读确认未扣材、技能未变化。Server 已按 `facilities` 的主条件及附加条件检查解锁记录，在扣材前拒绝未解锁请求；材料和技能券的拒绝样本现已对齐 HTTP 状态、错误正文及状态增量。正文使用 `ClientErrorResponse`，`httpStatus=409`、`errorCode=""`、`errorMessage=""`，经现有协议管线编码与加密。技能券重放基线仍有两条无关 Live 任务的用户字段省略差异。尚需玩家 Rank 30 且已解锁账号的成功样本核验正向流程；满级溢出和角色任务联动也待核验。

活动兑换取得中级技能券后，对二星卡投入一张也返回 HTTP 409；回读确认券和卡牌未变。本地旧实现曾扣券并将累计技能经验从 0 截到满级阈值 25，解锁检查已阻止该请求。这份拒绝样本不能证明官方的溢出处理规则。

Client 的 `skill-material.json` 示例携带 `requireReleaseConditionIds: [10030]`，写前回读缺少该条件时停止，不发送升级请求。使用前需替换为目标账号持有、技能未满级的卡牌，并确认材料库存。

Client 检查项目支持 `--replay-skill-practice <请求记录> <回读记录> <master目录> <输出目录>`。调用者须确认回读来自同一账号，且两次记录间未进行其他业务写操作。失败请求的 HTTP 状态和错误响应参与比较，缺失回读时状态增量标为未知，不视为全量删除；回读失败的状态码不覆盖原写请求结果。

## PUT `/api/user/{userId}/event-exchange/{eventExchangeId}`

- Path：当前 `userId`、`eventExchangeSummaries.eventExchanges[].id`，不是活动 ID。
- Query：`count` 为正整数兑换次数；无 body。
- Response：`obtainUserResources` 展示兑换所得，`updatedResources` 合并库存、兑换次数及任务状态。技能券奖励样本省略零值 `resourceLevel`。
- 请求时机与证据：`ScreenLayerEventExchange` 在玩家确认数量后调用 `PutUserEventExchangeAPI`；后者拼接上述路径并发送 PUT，成功后合并资源。
- master：兑换期、限额及成本来自 `eventExchangeSummaries`；奖励按 `resourceBoxPurpose=event_exchange` 与盒 ID 联合查询。

官方成功样本扣除 5000 活动代币、增加一张中级技能券，剩余兑换次数从 8 减为 7，状态仍为 `exchangeable`。同时新增对应活动的 `consume_event_item` 任务进度 5000，`isNewAchieved=false`；任务阈值来自 master。余额不足的后续请求返回 HTTP 409，重新认证回读确认代币、技能券、兑换记录和活动任务不变。返回列表中的无限量项省略 `exchangeRemaining`，不能补成零。

无限量金币兑换的连续两份样本均按次数扣代币、增加等量金币，状态保持 `exchangeable`，不返回剩余次数；奖励省略 `resourceId`。余额恰好归零后仍保留数量为 0 的活动代币记录。有限体力道具一次兑换完后返回 `exchangeRemaining=0`、`exchangeStatus=not_exchangeable`。三份样本的消费任务进度均按实际代币消耗累计。

售罄后重复兑换取得一份 HTTP 429，随后重新认证回读确认库存、兑换及任务状态未变。尚不能区分该状态码来自售罄条件还是限流，不据此实现通用拒绝规则。

Server 已接入兑换期内、已存在记录的技能券、体力道具和金币兑换，支持无限量、恰好售罄、余额归零，以及尚未达成的普通消费任务进度。已售罄后的重复兑换、超过剩余次数、兑换期外、缺少兑换记录、任务达成与其他奖励暂返回本地 501，不代表官方错误码；目录初始化和活动任务领奖待补。奖励数量溢出会回滚整次操作。

Client 提供 `event-exchange`、示例场景和 `--replay-event-exchange`。示例 ID 须按当前 master 和账号库存调整。已取得成功样本的相关 HTTP、基线、响应和状态增量对拍一致；金币比较覆盖 `userGamedata.coin`，完整背景资源不在此结论范围内。

## PUT `/api/user/{userId}/character-costume-3d/character/{characterId}/unit/{unit}`

- Path：当前 `userId`、角色 `characterId`、组合 `unit`。客户端把组合枚举名转大写后拼入路径。
- Body：`UserCharacterCostume3DRequest`，包含 `headCostume3dId`、`bodyCostume3dId`、`hairCostume3dId` 三个整数。
- Response：`SuiteUserCommonResponse.updatedResources`。客户端成功回调调用 `UserDataManager.UpdateAll` 合并状态。
- 官方首次更换样本：返回完整 `userCharacterCostume3ds`，其中组合名为小写；只修改对应角色与组合的穿戴。新手任务 `change_any_character_costume` 达成，当次进度响应 `isNewAchieved=true`，Suite 回读为 false。未刷新服装持有列表，未发现角色任务变化。
- 官方 Suite 中，`userCostume3dStatuses` 的 `forbidden` 和 `sale` 状态省略未设置的 `obtainedAt`；`available` 的已有取得时间保留。

Server 已接入已有穿戴槽位保存、角色与部位匹配、持有检查及新手任务联动；当前支持 master 明确绑定该角色的服装。Client 提供 `character-costume-save` 和 `--replay-costume`。首次身体服装更换、同值保存、换回原服装三个样本在穿戴、持有列表、新手进度与任务状态范围 HTTP 对拍无差异。

官方同值保存返回 200，仍刷新穿戴和新手任务进度。服装任务已达成但未领奖时，每次保存仍加 1，已观察到进度 1 → 2 → 3；不会重复设置达成提示，也不刷新未变的任务状态。领奖保留进度 3，任务状态改为 received，总任务进度加 1；随后同值保存继续加至 4，保持 received，不重复提示。这与普通 Live 任务达到门槛后停止累加的行为不同。领奖及领奖后保存样本的相关字段 HTTP 对拍通过；跨角色共用服装、发型或配饰切换及无效请求的官方响应仍待核验。

证据：`PutUserCharacterCostume3DAPI` 构造、Execute 与回调，请求和穿戴模型契约、`costume3ds`、`beginnerMissionV2s`，以及官方更换和 Suite 回读样本。

## POST `/api/user/{userId}/costume-3d-shop/{shopItemId}`

- Path：当前 `userId` 与服装商店商品 `shopItemId`；无 query 和 body。客户端确认制作后发送请求。
- Response：`UserCostume3DShopResponse`，包含 `consumedCosts`、`obtainedResources` 和 `updatedResources`，用于显示制作结果并刷新用户数据。
- 官方身体服装制作样本：按 `costume3dShopItems.costs` 扣除材料，发放所选身体服装；已有 `sale` 记录转为 `available` 并写入 `obtainedAt`，商品转为 `sold_out`，未自动更换穿戴。
- 同步推进 `make_any_costume`、对应角色的 `collect_costume_3d` 和荣誉收集进度。新手 `isNewAchieved` 和角色 `achievedMissions` 为当次响应提示，不持久化。
- 材料消耗省略零值 `resourceLevel`，服装奖励保留 `resourceLevel=0`。

Server 当前接入普通身体服装及同角色头饰组合制作，Client 提供 `costume-craft` 和 `--replay-costume-craft`。官方组合样本按头饰、身体顺序发放，两件取得时间相同；只扣一次商品材料，角色服装收集和荣誉进度各加 1。两类样本的材料、服装、商店及三类任务字段 HTTP 对拍一致。

荣誉进度现复用商店章节所述通用称号门槛规则，达到门槛不再拒绝制作；收集门槛官方样本、部分组件已持有时的处理及失败请求错误码仍待核验，需要对应制作响应和前后 Suite。事务失败不保留扣材、服装或任务变化。

证据：`PostUserCostume3DShopAPI`、`UserCostume3DShopResponse`、服装商店和任务 master，以及官方制作与 Suite 回读样本。

### 音乐商店购买

`POST /api/user/{userId}/shop/{shopId}/item/{shopItemId}`，无 body。客户端 `MusicShopDetailDialog.OnClickOK` 调用 `PostUserShopAPI`，成功后合并刷新数据并进入购买结果展示。

官方在售歌曲样本确认：按 `shopItems.costs` 扣除音乐卡，通过 `shop_item` 资源盒解锁歌曲及 `musicVocals` 中条件为默认开放的音源；对应商品从 `sale` 变为 `sold_out`。刷新返回完整 `userMusics`、`userMusicVocals`、`userShops`、材料及新手任务相关数组。无等级商品省略 `level`；歌曲兑换任务在首次购买后达成，当次响应 `isNewAchieved=true`，Suite 回读为 false。

Server 已接入歌曲购买的在售与持有检查及新手任务联动。Client 使用 `shop-purchase`，并提供 `--replay-music-shop`：单个官方成功样本的歌曲、音源、商店、材料、新手任务进度和状态范围 HTTP 对拍无差异。重复购买、材料不足、商品时间边界及非默认音源的额外解锁仍待官方样本核验。

### 新手任务重复领奖

`PUT /api/user/{userId}/mission/beginner_mission_v2`，body 为 `missionIds` 数组。官方已达成任务首次领取成功；对同一已领取 ID 再次请求返回 HTTP 409，正文为 `httpStatus=409`、`errorCode=""`、`errorMessage=""`。随后回读确认材料、新手任务进度和任务状态均不变。

Server 已在用户事务内检查已领取状态，拒绝时回滚整次操作；Client 的 `--replay-beginner-repeat` 对齐该拒绝样本的 HTTP 状态、错误正文，并检查本地用户状态不变。未达成或不存在的任务也不允许发奖，但这些输入的官方错误响应尚未核验。包含已领取任务的混合批量请求仍待核验。

### 新手任务成功领奖与总任务进度

单领普通任务和批量领取两个普通任务的官方样本确认：`achieve_all_missions` 进度分别增加 1 和 2，随 `userBeginnerMissionV2s` 刷新；未达总门槛时 `isNewAchieved=false`。计数发生在领奖时，不是普通任务刚达成时。批量奖励保留每个任务的资源记录，未把相同水晶奖励合并。材料奖励保留 `resourceId`，省略零值 `resourceLevel`；水晶奖励省略零值 ID 和等级。

Server 按 `beginnerMissionV2s` 中 `achieve_all_missions` 的完整门槛处理领奖计数，跨门槛时保存 `achieved`，保留已领取状态；不再拒绝最后一次普通任务领奖。总任务奖励仍需独立领取，通过 `mission_reward` 资源盒发放，完成类别不计入普通任务领奖数量。新达成提示只进入本次响应，独立 Suite 回读为 false。

泛化依据：官方单领与批量样本确定计数时机及增量；master 定义总任务类型、门槛和奖励；客户端 `BeginnerMissionV2View.GetReceivableMissionAll` 先收集普通可领取任务，仅在该列表为空且总任务已达成时添加总任务 ID 和对应奖励盒。总任务门槛处理复用新手任务通用达成规则。多门槛、重复 ID、已领取状态、不同资源奖励、编码失败回滚和正式 HTTP 的达成提示、独立回读、后续领奖均有本地检查。最终达成及完成奖励尚无官方成功样本，属于上述证据支持的规则推导，不能称为官方全流程验证。

Client 提供 `--replay-beginner-mission`。既有两份成功样本的材料、水晶、任务进度、状态和奖励响应 HTTP 对拍无差异；其他背景字段不在该结论内。

新增五任务批量样本确认总进度4→9、免费水晶增加500，额外Suite保持结果；完整奖励数组、所选任务及资源响应、持久变化对拍一致。回放导入官方写前快照后直接发出业务请求，再读取一次Suite，避免额外写前Suite重复触发好友计数。该样本的活动任务状态不含用户ID，响应编码省略其零值用户ID，仍保留显式非零值；不通过忽略比较字段掩盖差异。

### 普通 Live 结算：新手演出任务

`PUT /api/user/{userId}/live/{userLiveId}` 的普通手动演出成功样本中，`beginnerMissionV2s` 的 `any_live_clear` 任务每次增加 1，达到 master 的 `requirement` 后停止增长。已核验任务的门槛为 3，第三次结算新增 `beginner_mission_v2` 的 `achieved` 状态。

响应 `updatedResources.userBeginnerMissionV2s` 返回完整进度数组；仅新达成的任务在该次响应中 `isNewAchieved=true`，随后 Suite 回读为 false。达成后的再次结算不返回该进度字段，也不重复刷新任务状态。提示字段只在响应副本中填充，不写入存档。

Server 已实现上述普通手动 Live 联动，Client 结算重放覆盖新手任务进度。首次、达成时及达成后的三个官方样本，在新手任务、Live 任务进度与任务状态范围 HTTP 对拍无差异。Auto、失败演出、挑战 Live 和其他新手任务类型不属于本次核验范围。

### 普通 Live 结算：角色次数与角色任务

同一结算接口的手动成功样本中，按编队 `leader` 卡牌对应的 `cards.characterId` 累计 `userCharacterLiveUsageCounts`：`characterLiveUsageType=leader` 的 `usageCount` 从 4 增至 5；对应 `play_live` 角色任务也从 4 增至 5。刷新保留次数数组中的其他记录。客户端 `ScreenLayerChallengeLiveCharacterSelect` 使用队长次数和 `challengeLiveCharacters.releaseConditionId` 对应的数量门槛显示挑战解锁进度。

Server 已接入成功结算的队长次数及角色演出任务，二者与其他结算状态共同提交；编码失败回滚，重复结算不再次计数。Client 的 `--replay-live` 输出 `leader-usage-compare.json`，上述样本的队长次数、角色任务基线、响应及状态增量一致。

五个不同角色的编队另有换位对照：原顺序仅前四位增加 `member` 次数；交换第四、第五位后，新第四位角色从无记录变为 1，新第五位保持原次数，前三位及队长各加 1。两次均为普通手动成功结算，换位样本开局后等待 180 秒再结算，随后删除临时编队并回读确认恢复。Server 按该结果累计前四个成员位，队长同时保留独立 `leader` 记录；`character-usage-compare.json` 检查完整次数数组及角色任务，两份 HTTP 重放无差异。

两份 C 档 Auto 成功样本中，队长次数、前四位成员次数及队长的 `play_live` 任务均增加 1；两份手动失败样本中三者不变，响应省略未变化的次数和任务字段。Server 按 `life > 0` 累计，不再排除 Auto；四份样本的完整角色次数与任务 HTTP 重放无差异。Auto 编码失败回滚和重复结算另有本地检查。

重复角色编队的官方保存尝试返回 400，未进入演出，拒绝原因及其计数规则尚未确认；本地该结算分支暂不支持。Auto 失败、其他评分和达到挑战解锁门槛后的联动仍待核验。

## PUT `/api/user/{userId}/login-status`

- Path：当前 `userId`；无 query。Body：`PutUserLoginStatusRequest.loginStatus`，按 `LoginStatus` 枚举名称发送字符串。
- 官方接受 `offline`、`online`、`solo_live`、`multi_live`、`challenge_live`、`cheerful_live`、`virtual_live`、`rank_match`、`own_mysekai`、`other_mysekai`，成功返回加密空 map `{}`。客户端虽声明 `SuiteUserCommonResponse`，实测没有 `updatedResources`。未知字符串返回 400、零字节正文。
- `LoginStatusKeeper.Run` 可立即发送状态，随后轮询维持；轮询间隔为 `max(master 过期秒数 - 60, 120)`。`solo_live`／`challenge_live` 使用 solo 配置，multi／cheerful／rank 使用 multi，virtual 单独配置，其余使用 online；当前四项 master 均为 600 秒。
- 状态不在自己的 Suite 顶层，而在好友记录的 `userLoginStatus` 中。公开状态包含 `loginStatus` 和 `loginStatusUpdatedAt`，显式 offline 也保留时间戳。关闭 `userConfig.isDisplayLoginStatus` 时，对方收到 offline 且省略时间戳；不会据此覆盖真实状态。
- 认证、完整 Suite 和好友 parts 查询都会把当前用户重新标记 online。因此 Client 的 `login-status-save` 禁用自身前后 Suite 快照，验证改用另一账号读取好友列表。该行为不意味着所有 GET 接口都已确认会标记在线。

证据：`PutUserLoginStatusAPI.Execute`（RVA `0x6184380`）、请求契约、`LoginStatusKeeper`、`FriendViewData.SetupLoginStatus`（RVA `0x6362f8c`）、master 配置及两个测试账号的官方对照。好友展示代码在超过更新时间加过期时长后显示离线；官方超时后的原始返回值尚待采样，服务端目前保留原始状态和时间戳，由客户端处理已确认的展示过期规则。

Server 在用户事务中保存私有状态，认证和 Suite 入口提交在线状态，查询好友时读取对方当前快照；隐藏状态的时间戳仅在响应层省略。5 组 HTTP 对拍覆盖公开 online／solo_live、显式 offline、隐藏状态及非法输入后的另一账号回读；本地检查另覆盖编码回滚和引用隔离。未证明完整好友资料、超时、所有入口的自动状态变化或游戏场景切换均已对齐。

观察工具另外支持 POST／PUT `/api/user/{userId}/friend/{opponentUserId}`，分别为 `friend-request` 和 `friend-approve`；申请 body 使用 `message` 和 `friendRequestSentLocation`，接受申请无 body。已实测空消息、`id_search` 来源的测试账号申请与接受，成功响应含 `updatedResources`；对方 ID 为正 64 位整数。申请方为 `sent_request`，接收方为 `pending_request`，样本到期时间为响应 now 加 7 天；接受后变为 `friend`，增加 `approvedAt` 并保留原到期时间。空消息样本省略 message。

## POST／PUT `/api/user/{userId}/friend/{opponentUserId}`

- Path：当前账号和对方的 64 位 ID；无 query。POST body 为 `message`、`friendRequestSentLocation`；PUT 无 body。
- 请求时机：申请弹窗提交 POST，接受操作提交 PUT；成功回调通过 `UserDataManager.UpdateAll` 合并 `SuiteUserCommonResponse.updatedResources`。申请弹窗先检查消息长度及 NG 词。
- 成功返回完整 `userFriends` 列表。首次申请建立双方 `sent_request`／`pending_request`，期限为 now 加 master 的 168 小时；空消息省略，普通日文消息原样保留。
- 重复申请返回 200，更新双方消息和期限，不追加记录；重发空字符串清除原消息，响应省略 message。接收方反向 POST 相当于接受，双方成为 `friend`，增加相同 `approvedAt`，保留原消息和期限。PUT 接受具有相同关系结果。
- 无已有关系时，接收方 `friendRequestScope=reject` 返回 409，错误码 `opponent_friend_request_scope_reject`；`id_search` 范围接受同名来源，`multi_live` 来源返回 409、`opponent_friend_request_scope_id_search`。错误体 errorMessage 为空；双方独立回读没有新关系。实现按是否为 id_search 判定来源匹配，其余来源尚未逐项联网采样。
- 范围允许时，双方已经是好友再 POST 或 PUT 返回 409；命中已采样 NG 词的消息返回 400。这些样本的错误体为 `httpStatus`、空 `errorCode`、空 `errorMessage`，独立回读无关系变化。客户端 NG 检查按 `String.Contains` 匹配 master 词条；消息上限来自 master 的 30 字符配置。
- POST 的范围校验先于已有关系处理：接收方改为 reject 后，原申请重发及反向申请都返回范围错误，保留原消息和期限；已是好友时也优先返回范围错误，id_search 与 multi_live 不匹配同样如此。直接 PUT 接受已有申请不检查拒收新申请的配置，双方都为 reject 时仍可接受。
- 好友卡牌仅含 `cardId`、`level`、`masterRank`、`specialTrainingStatus`、`defaultImage`。默认队长头像及特训前／后的自选卡牌头像均已对拍：`userProfile` 描述头像选择，`userCard` 始终取当前主队队长卡，即使自选头像是另一张卡。客户端 `FriendViewData` 将其构造成 `LeaderCardInfo`。当前资料覆盖普通称号及其任务进度、空边框、未设置 Mysekai 访问；查询使用对方当前快照，响应投影不写回存档。未接受记录省略 `approvedAt`。

证据：`PostUserFriendRequestAPI.Execute`（RVA `0x6179364`）、`PutUserFriendApprovalAPI.Execute`（RVA `0x6179668`）、请求／响应契约、申请弹窗、NG 检查 lambda（RVA `0x627e174`）、master 配置及三个测试账号的官方写入与双方回读。

Client 已提供申请、接受、好友 parts 操作；Server 通过 `UserOperation.ExecutePair` 原子提交双方关系。6 组 HTTP 对拍覆盖空消息、日文消息、重复申请、反向申请、接受和 NG 拒绝，比较完整好友列表及双方独立回读；回读中的在线状态因会话刷新单独排除，写响应仍比较该字段。业务检查覆盖双边回滚、重复记录及响应隔离。未启动实际游戏。

另有 5 组 HTTP 对拍覆盖重发消息替换／清空、reject 范围拒绝、id_search 匹配成功及 multi_live 不匹配拒绝，错误码、消息省略、期限和双方列表无差异。范围设置通过现有 `user-config-save` 操作采样；采样后已恢复原设置。

范围优先级再以 5 组 HTTP 对拍确认：已有申请重发、反向 POST、直接 PUT 接受、已是好友的 reject／id_search 拒绝。修正了先反向接受或先返回通用好友冲突而跳过范围校验的问题。申请被拒绝后双方关系不变；直接接受则原子提交双方 friend 状态。

待核验：自身或未知目标、其他来源、无效消息与范围限制的错误优先级、已拒绝或过期申请、数量上限，以及多种任务称号、羁绊称号、边框、Mysekai 资料映射。未知状态保留未支持分支，不能将当前实现视为完整好友系统。

好友普通称号补充：`userProfileHonors` 按当前装备返回，保留槽位、称号等级、背景与文字，未设置的可选字段仍省略。master 未定义 `honorMissionType` 时不关联任务。单称号、双称号、带背景／文字及清空共 4 组双账号 HTTP 对拍无差异，响应投影与存档引用隔离。

好友任务进度补充：`FriendViewData` 使用两张列表构造 `HonorInfo`；`HonorUtility.TryFindUserHonorMission`（RVA `0x4daf600`）及其 lambda（RVA `0x4db2a40`）按 master 的 `honorMissionType` 匹配任务。官方称号 3009 样本仅返回 `userHonorMissions=[{honorMissionType:"easy_full_combo",progress:5}]`，省略 `userId`、`achievedMissionIds`，不带账号其他任务。单独装备、混合普通称号、清空共 3 组双账号 HTTP 对拍通过，清空后任务列表为空。Server 依据装备关联类型投影当前进度；多种进度称号并存时的排序、缺失任务记录、羁绊称号仍需样本。

获取样本时新增四首不同 Easy FC，进度从 1 逐次增至 5。跨门槛结算响应临时携带 `achievedMissionIds=[10001]`，随后 Suite 回读为空；任务状态为 `achieved`，领取后为 `received` 并获得称号 3009 等级 1，领取 HTTP 对拍通过。普通单人 Live 的 Easy FC 累计现已接入，范围见下文；其他称号任务尚未全量实现。

### 普通单人 Live 的 Easy FC 称号进度

- 沿用 `PUT /api/user/{userId}/live/{userLiveId}`。开局的 `musicDifficultyId` 查 master 确定难度；结算依据非 Auto、成功存活与 FC 判定，并在更新成绩前检查该曲 Easy 的历史 `fullComboFlg`。
- 首次完成的不同 Easy 曲目将 `userHonorMissions.easy_full_combo.progress` 增加 1；同曲再次 FC、Auto，以及已有失败结算样本均不增加。仍刷新现有 `userHonorMissions`，此列表中的其他类型不属于本轮统计实现范围。
- 达到 `honorMissions.requirement` 后新增 `honor_mission` 的 `achieved` 状态；已达成或已领取的任务不重复标记。响应的对应任务 `achievedMissionIds` 仅含本次新增 ID；存档及随后 Suite 保留空数组，`userId` 省略。
- 进度、成绩与任务状态在同一用户操作中提交，响应编码失败整体回滚。Controller 只在响应副本上填写临时提示，不写入持久任务。

四次新增 FC（含首次门槛）、同曲重复、Auto、已有失败样本共 7 组 HTTP 对拍通过，覆盖 Easy FC 进度、称号任务状态、判定标志、临时提示及独立回读。Client 检查支持 `--replay-live-honor`。不据此声明全量 Live 响应一致：金币收集、指定生命值等其他称号进度仍有缺口；其他难度、AP、多人、挑战与自定义谱面需各自证据。跨门槛后更高等级领取的奖励升级仍待核验。

证据：`PutUserLiveClearAPI` 请求、成功回调对 `updatedResources` 的合并，`UserHonorMission` 契约、`musicDifficulties`／`honorMissions` master，以及上述官方结算和 Suite 回读。

### 普通单人 Live 的 Normal FC 称号进度

Normal FC 补充：同一普通结算入口中，首次成功手动完成某曲 Normal FC 新增 `normal_full_combo.progress=1`；同曲再次 FC 保持 1，两个样本的 Suite 回读一致。Server 按曲目和难度检查旧 FC 标志，再更新对应称号进度；同曲 Easy 成绩不会阻止首次 Normal 计数，原有 Easy 路径保留。客户端 `MusicUtility.CheckAnyUserMusicFullCombo` 按曲目、难度查询记录，并检查 `fullComboFlg`；多人已有 FC 的跨模式情形尚无官方样本。

`--replay-live-honor` 已纳入 Normal 类型；首次、重复及旧 Easy 回归三组 HTTP 对拍通过。master 中 Normal 的首个任务 11001 要求 5 首；当前官方进度仅 1，未达成该任务。独立进度、重复去重与按门槛达成有小型夹具检查，不能将其称为官方跨门槛证明。Normal Auto、失败、5 首门槛与领奖仍需各自采样。

### 普通单人 Live 的通关与生命耗尽次数

- 同一结算入口中，`life > 0` 的手动或 Auto 演出增加 `clear_live`；同曲重复成功也增加，不采用 FC 的曲目去重规则。失败不增加通关次数。
- 手动生命耗尽结算增加 `finish_live_with_empty_life`。官方从 4 到 5 的样本达成任务 1302，响应仅在该次显示 `achievedMissionIds=[1302]`，Suite 回读为空；状态保存为 `achieved`。下一次失败继续增加至 6，提示数组为空，不重复刷新任务状态。
- 这两类计数与 Easy FC 复用按 master 类型查询门槛的任务逻辑，全部状态在同一用户操作中提交；编码失败和重复已结束会话不会重复累计。
- `clear_live` 当前 master 门槛为 39、3939。通关计数的手动、重复和 Auto 已有官方样本，39 次首次达成本轮仅有 master 及小型夹具验证，尚未官方采样。不得把生命耗尽的门槛样本当成通关门槛证明。

手动成功、同曲重复、Auto、旧失败、生命耗尽跨门槛及达成后共 6 组 HTTP 对拍通过。`--replay-live-honor` 同时比较 Easy FC、通关、生命耗尽三类进度、称号任务状态、提示和独立回读。其他 Live 类型与 Auto 生命耗尽组合未确认；该检查不包含其他称号进度或完整结算奖励。

### 普通单人 Live 的最高连击与通关等级

- 手动成功结算将 `clear_live_combo` 更新为历史进度与请求 `maxCombo` 的较大值。它不是累计连击，也不要求 FC：一次带 Good、`fullComboFlg=false` 的 1000 连击成功结算达成任务 1401；此前 1100 连击失败结算没有改变原有 210 的进度。
- 同一成功结算将 `play_level_clear` 更新为历史进度与 `musicDifficulties.playLevel` 的较大值。等级 5→27 的官方样本一次达成 master 中要求 15～27 的 13 个任务。不得把谱面等级误用为用户等级或难度 ID。
- 新达成 ID 仅进入本次对应类型的 `achievedMissionIds`；任务状态持久化，随后 Suite 的提示列表为空。后续成功 1100 连击继续提升纪录，已达成的任务不重复提示；相同 Level 27 不重复刷新任务状态。
- 失败演出不更新这两种纪录。高于手动最高等级的 Level 31 Auto 样本也不更新手动的 Level 27 或连击纪录；低于既有纪录的手动样本不降低进度。

高连击失败、跨门槛成功、更高连击成功、较低连击成功、普通 Auto、高等级 Auto 共 6 组 HTTP 对拍通过。`--replay-live-honor` 扩大到上述五类进度，并保留完整 `honor_mission` 状态比较及独立回读。首次对拍暴露的等级任务缺口已补齐；不宣称其余称号进度和全量奖励一致。实现复用称号门槛与事务流程，本地检查覆盖一次跨多个门槛和编码失败回滚。

证据：`ScreenLayerFreeLiveResult` 将演出结果的最大连击写入请求，`UserLiveClearRequest.maxCombo` 契约位于 offset `0x28`，以及 master、上述成功／失败／Auto 样本。其他 Live 模式和自定义谱面仍需各自样本。

## DELETE `/api/user/{userId}/friend/{opponentUserId}?type={type}`

- Path：当前账号和对方的 64 位 ID；无 body。Query `type` 的已确认映射：`cancel_friend_request` 取消自己发出的申请，`reject_friend_request` 拒绝收到的申请，`release_friend` 删除已建立的好友关系。
- 调用时机：好友界面取消申请、拒绝申请或解除好友；`FriendUtility.RejectRequestAsync` 使用对应 `DeleteFriendRejectAPI.UseCase` 发起请求。响应为 `SuiteUserCommonResponse`，成功回调合并 `updatedResources`。
- 对应正常状态下均返回 200，双方移除关系，响应包含当前账号剩余的完整 `userFriends`。拒绝申请不保留 `rejected` 记录；其他好友不变。
- 双方已经没有该关系时，三个 type 都返回 200，但 `updatedResources` 不包含 `userFriends`。不能为了返回空列表而清除其他好友，也不应伪造一次好友刷新。
- Client 操作为 `friend-cancel`、`friend-reject`、`friend-release`。Server 复用双账号事务；响应编码失败时回滚双方删除。

动作与当前关系的实测组合如下；关系状态以发起本次 DELETE 的账号为准，对方记录与之配对。

| 当前状态 | cancel_friend_request | reject_friend_request | release_friend |
| --- | --- | --- | --- |
| sent_request | 200，删除双方 | 409，空错误码 | 409，空错误码 |
| pending_request | 409，空错误码 | 200，删除双方 | 409，exists_pending_friend_request |
| friend | 200，删除双方 | 409，空错误码 | 200，删除双方 |
| 双方均无关系 | 200，不刷新好友 | 200，不刷新好友 | 200，不刷新好友 |

上述 409 均使用 `ClientErrorResponse`，errorMessage 为空，关系保持不变。不能把三个动作统一成无条件删除，也不能按界面动作名称推断“取消”必定拒绝已建立的好友。

证据：`DeleteFriendRejectAPI.Execute`（RVA `0x6178ed4`）、`UseCase` 枚举顺序、地址 `0xB09FC88` 起三个重定位项与字符串表交叉确认类型映射，回调 `OnReceivecResponce`（RVA `0x617910c`），以及官方写入和双方独立回读。

6 组 HTTP 对拍覆盖正常取消／拒绝／删除及无关系时重复操作，另 6 组覆盖表中的错方向或错状态动作，验证错误体、好友字段存在性、完整列表和双方回读。在线状态的回读排除规则与上一节相同。自身或未知目标、非法 type、过期关系及双方记录不一致仍待采样；实现对未确认的状态组合保留不支持分支。

## POST `/api/user/{userId}/config`

- Path：当前 `userId`；无 query。Body：`PostUserConfigRequest`，包含可空的 `defaultMusicType`、`isDisplayLoginStatus`、`friendRequestScope`。未指定或 null 表示保留原值，false 则明确关闭在线状态显示。
- 已确认值：默认音源为 `sekai`／`original_music`；好友申请范围为 `all`／`id_search`／`reject`。
- 客户端调用：`OptionDialog.UpdateServerData` 比较在线显示和好友申请范围，变化时调用 `UserInformationUtility.UpdateUserConfig`；该调用将默认音源置 null。当前默认音源选择还使用本地设置，不能据此断言所有设置都通过此接口保存。
- Response：官方同时返回顶层 `userConfig` 和 `updatedResources`。非 null 字段请求会在刷新中返回完整 `userConfig`，相同值重复保存也返回；全 null 或空请求只在顶层返回配置，刷新中省略该字段。客户端声明的 `SuiteUserCommonResponse` 只读取 `updatedResources` 并合并用户状态，未声明顶层配置。
- 未知好友范围、未知默认音源均返回 400、零字节正文；与合法的在线显示字段混合提交时整体不修改，独立 Suite 回读确认。

证据：`PostUserConfigAPI.Execute`（RVA `0x618c438`）及成功回调、`PostUserConfigRequest`／`UserConfig` 契约、`FriendRequestStatus` 和 `DefaultMusicType`、`OptionDialog.UpdateServerData`（RVA `0x4c9accc`）、`UserInformationUtility.UpdateUserConfig`（RVA `0x4e06444`），以及官方请求、响应和回读。

Client 已接入 `user-config-save` 和 `--replay-user-config`；Server 复用用户事务与配置模型，并补充抓包确认的顶层响应。10 份样本在完整配置、响应及独立回读范围 HTTP 对拍通过；编码失败回滚另有本地检查。空字符串、大小写变体、缺失请求体和其他非法类型尚待官方样本，完整游戏设置页未运行验收。

## POST `/api/user/{userId}/music-video/{musicId}`

- Path：当前 `userId`、歌曲 `musicId`；无 query。Body：`UserMusicVideoRequest`，键为 `musicVocal`（音源 ID）、`musicPlayStatus`（`start`／`end`）、`musicCategoryName`（`mv`／`mv_2d`／`image`／`original`）。
- Response：`SuiteUserCommonResponse.updatedResources`；客户端成功回调通过 `UserDataManager.UpdateAll` 合并用户状态。
- 请求时机：MV 确认页保存观看信息，返回 OutGame 时提交 `end` 并清除该信息；提前退出的 `MusicVideoController.OnRetire` 会清除信息。API 定义支持 `start`，尚未定位正常观看流程中的实际调用点。
- `start` 不增加任务进度。有效 `end` 按 `musicVocals.musicId` 和 `musicCategories` 校验组合，增加 `watch_any_music_video_full` 新手任务进度；不要求先发送 `start`，也不要求持有歌曲或音源，不会解锁它们。
- 首次达成刷新任务状态，并在本次返回的进度中设置 `isNewAchieved=true`；Suite 回读为 false。达成和领奖后继续计数，不封顶，不重复刷新任务状态；已领取状态保持不变。
- 未知歌曲、未知音源、音源所属歌曲不符或歌曲没有该类别时，官方返回 200，但不增加任务进度。未知播放状态、未知类别以及 `none` 返回 400、零字节正文；不能仅依据客户端枚举接受 `none`。

证据：`PostUserMusicVideoAPI` 构造、Execute（RVA `0x618ed5c`）及回调，`MusicUtility.ExcuteMusicVideoAPI`、MV 确认与返回流程、`MusicVideoController.OnRetire`（RVA `0x5441904`）、请求模型和两张 music master 表，以及官方样本。

Client 提供 `music-video` 场景，Server 通过用户事务累计任务，响应编码失败回滚；空错误体采用该接口独立标记。19 份样本覆盖首次与重复结束、3D／2D／image／original、未持有歌曲及 Another Vocal、领奖后计数、无效组合和三种拒绝；任务、歌曲与音源持有、材料、水晶及对应响应字段的 HTTP 对拍通过。三次拒绝后独立官方 Suite 回读在上述范围内不变。该结论不包含完整游戏播放、缺失请求字段、其他任务类型或全量 Suite；这些行为需要对应调用链和官方样本补充。

## PUT `/api/user/{userId}/mission/live_mission`

- Path：当前 `userId`；无 query。Body：`UserMissionReceiveRequest.missionIds`，待领取的任务 ID 数组。
- Response：`UserMissionReceiveResponse`；`obtainedRewards` 为获得的资源，`updatedResources` 为用户刷新数据。
- 请求用途：领取已达成的 Live 任务。`PutUserMissionReceiveAPI` 使用任务类型拼接路由，成功回调合并用户刷新数据，再通知调用方。
- 官方免费任务成功样本：返回金币奖励，任务状态从 `achieved` 变为 `received`；刷新包含完整 `userMissionStatuses`，各条省略 `userId`，不包含 `userLiveMissions`。金币资源省略零值 `resourceId`、`resourceLevel`。
- 普通 Live 的任务达成样本：进度从 90 增至 120，跨过 master 的 100 门槛后新增 `achieved` 状态。本次结算的 `userLiveMissions.achievedMissionIds` 包含新达成 ID，随后 Suite 回读为空；后续 120 至 150 不重复提示，也不重复刷新任务状态。`userLiveMissions` 省略 `userId`。

Server 已补免费任务达成及结算提示，Client 支持领奖场景和 `--replay-live-mission` 样本重放。门槛前、跨门槛及达成后的三次普通 Live HTTP 重放，在任务进度、任务状态和对应响应字段范围无差异；领奖样本在金币、任务进度、任务状态及奖励响应范围无差异。全量背景字段、其他奖励种类、批量及重复领奖、付费通行证和周期边界仍待官方核验；本地防重复测试不代表官方重复请求行为已确认。

普通 Live 与挑战任务共用时间周期查询：`liveMissionPeriods.startAt <= timestamp < endAt`，不按通行证或周期最大 ID 选择。依据为 `MasterDataManager.GetMasterLiveMissionPeriodNow`（RVA `0x62aad00`）及 `GetMasterLiveMissionPeriod`（RVA `0x62aad60`）；无匹配返回 0。本地检查覆盖起止边界、空档和未来大 ID；普通 Live 官方样本的任务 HTTP 回放一致。跨月开局后结算仍待官方样本确认。

证据：`PutUserMissionReceiveAPI` 构造、Execute 和回调，任务请求与响应契约、`liveMissions` 与 `mission_reward` 资源盒，以及免费任务达成、领奖和回读样本。

## PUT `/api/user/{userId}/mission/honor_mission`

- Path：当前 `userId`；无 query。Body：`UserMissionReceiveRequest.missionIds`，称号任务 ID 数组。
- Response：`UserMissionReceiveResponse.obtainedRewards` 用于奖励展示，`updatedResources` 合并用户状态。客户端通过通用 `PutUserMissionReceiveAPI` 按任务类型拼接路由，在领取达成任务时请求。
- 首次领取已达成的任务 3911：返回称号 532、等级 1、数量 1，任务状态由 `achieved` 改为 `received`。刷新包含 `userMissionStatuses`、`userHonors`、`userProfile`、`userProfileHonors`；任务状态条目省略 `userId`，不刷新 `userHonorMissions`。
- 已领取、未达成、混合已达成与未达成、重复 ID 四种请求均返回 409，`errorCode` 和 `errorMessage` 为空。失败后独立 Suite 回读确认任务状态、称号进度与持有列表不变。重复 ID 不按 Live 任务的本地去重逻辑处理。
- 奖励依据 `honorMissions.rewards.resourceBoxId` 查询 `resourceBoxPurpose=mission_reward` 的资源盒，不能只按资源盒 ID 查询。

Client 提供 `honor-mission-receive`；Server 通过用户事务发奖与更新状态，编码失败整体回滚。五组官方 HTTP 对拍覆盖首次成功和四种拒绝，在奖励、称号持有、装备、资料、任务状态及进度范围一致；未比较完整 Suite。空数组、未知 ID、多项成功、已有称号升级及其他奖励组合仍待对应官方样本，尚未确认的错误形态不作为官方结论。

任务 3911 的奖励称号 532 本身没有 `honorMissionType`，不能用于证明好友资料中的任务进度映射。带进度称号还需实际获取、装备和好友回读样本。

证据：`PutUserMissionReceiveAPI` 构造、Execute 与成功回调，`MissionType`、请求响应契约、`honorMissions`、`resourceBoxes`，以及五组领取与独立回读样本。

## PUT `/api/user/{userId}/home/refresh`：登录奖励分支

- Path：当前 `userId`；无 query。
- Body：`UserHomeRefreshRequest.refreshableTypes` 为字符串数组，登录奖励类型为 `login_bonus`。
- 请求时机：`HomeUtility.GetHomeAPIRefreshableTypes` 在 `UserDataManager.LoginBonusStatus` 有效时追加 `login_bonus`，同时追加感谢消息刷新类型；类型由 `RefreshableType` 转为字符串。在线状态更新接口不用于领取登录奖励。
- Response：客户端读取 `UserHomeRefreshResponse`，以 `updatedResources` 合并状态，顶层 `userLoginBonuses` 供登录奖励展示。每条奖励包含 `userId`、`loginBonusId`、`loginBonusType`、`progress`、`receivedAt`、`displayTexts`。
- 奖励类型包含 `normal`、`beginner`、`limited`。响应还有其他刷新类型对应字段，不能将所有刷新统一当作登录发奖。
- 证据：`HomeUtility.GetHomeAPIRefreshableTypes`、`PutUserHomeRefreshAPI` 构造、Execute 与回调，以及 `UserHomeRefreshRequest`、`UserHomeRefreshResponse`、`UserLoginBonus` 契约。

Server 已接入首次登录发奖及重复刷新保护，Client 提供对应场景与官方记录重放。奖励按 master 写入邮箱，不直接增加背包；邮箱、登录记录及首次荣誉任务进度在同一用户操作内提交。现有实现只支持已核验的新手及限时赠礼文案，认证、完整 Suite 和首页刷新已返回首次领取状态头；后续日次、跨日推进及活动边界仍待实现与核验。

官方首次刷新样本：普通、新手及两组限时登录记录的 `progress` 均为 1，`receivedAt` 相同，`displayTexts=[]`，保留 `userId`；顶层依次展示普通、新手、限时奖励，后续 suite 按类型及 ID 排列。邮箱新增 10 项，资源种类、ID 和数量与 `login_bonus` 奖励箱展开结果一致；样本中赠礼有效期为发放后 30 天，`seq` 等于有符号 64 位最大值减 `grantedAt`。首次与重复刷新均更新 `lastLoginAt`；重复刷新顶层 `userLoginBonuses=[]`，已保存登录记录及邮箱不变。`X-Login-Bonus-Status` 在首次刷新前为 true，刷新后为 false。首次及重复样本的登录记录、邮箱、荣誉任务和玩家数据范围重放无差异；不代表全量背景字段或完整登录周期已对齐。
