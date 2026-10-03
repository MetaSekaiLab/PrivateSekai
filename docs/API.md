# API

**DO NOT SHARE!!**

**不要分享！！**

[MetaSekaiLab/PrivateSekai](https://github.com/MetaSekaiLab/PrivateSekai) by [MetaMiku](https://github.com/MetaMikuAI)

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
- Body `profileImageType`: 资料头像类型；普通资料保存流程会发送默认头像类型字符串。
- Body `profileImageId`: 资料头像 ID，可为空。

### 返回字段

- `updatedResources`: `SuiteUser` 局部更新数据；客户端成功后会合并到本地用户数据，主要用于刷新 `userProfile` 相关状态。

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

Server 已补卡面和任务联动，Client 支持 `--replay-special-training`。该样本的相关响应与增量一致；导入基线仍有一项无关 Live 任务的 `userId` 字段差异。非空特殊奖励、重复特训及其他稀有度尚需官方样本；非空奖励返回字段不按空数组样本推定。`special-training.json` 中的卡牌须替换为目标账号持有、未特训且已达等级上限的卡，并准备 master 指定材料。

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
- `ChallengeLiveUtility` 按各阶段所需点数累加和定位当前阶段，达到门槛即进入下一阶段。普通阶段样本验证了跨级和剩余点数；EX 阶段仍需独立核验。
- 两次 C 档样本的 `userLivePoint.addNormalProgress=30`、`addDailyBonusProgress=0`，与 `configs.obtain_live_point_for_challenge_live` 一致；`livePointBonusRemaining=3`。其中一份样本的 `userLiveMissions.progress` 从 150 增至 180，`paidProgress` 仍为 0。两份样本的 `userBoost` 均未变化；其他评分、付费状态和跨日情形仍待核验。

当前结算样本使用模拟输入。三次 C 档样本中，7999 分获得 200 点，8000 与 10000 分获得 201 点；玩家经验均增加 4000。双卡样本的两张卡各增加 12000 经验，响应 index 为 1、2，与领队、支援位对应。部分评分奖励材料 ID 不同，发放规则仍待核验。完整点数公式、其他评分经验、奖励抽取、跨日和自动挑战尚未核验，不能把单次返回值用作固定公式。

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
- 官方样本：兑换项 2、成本组 1、次数 2，扣除材料 15 共 200，增加中级练习券 30；新增兑换记录，`exchangeCount=2`、`totalExchangeCount=2`、`exchangeStatus=exchangeable`，并记录 `lastExchangedAt`。数值均与 master 消耗组及 `material_exchange` 资源盒一致。

Client 已支持 `material-exchange` 和示例场景，要求明确提供整数消耗组与正整数次数。Server 尚未接入；周期刷新、限量、关联兑换和失败行为仍待核验。示例兑换项须按当前 master 和账号库存确认。
