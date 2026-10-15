# 童印 .NET 后端最小整洁架构

项目采用 .NET 10，按 **Domain / Application / Infrastructure / Api** 四层组织。

- `Tongyin.Domain`：领域实体及业务规则；目前实现了作品创建及图片、模型生成阶段的领域状态转换，未接入外部服务。
- `Tongyin.Application`：提供 `CreateWork` 应用用例及 `IWorkRepository` 持久化端口，不依赖数据库实现。
- `Tongyin.Infrastructure`：使用 Dapper 实现 `SqlWorkRepository` 创建作品的 SQL 写入适配器，AI 接入未迁移。
- `Tongyin.Api`：已注册兼容小程序的作品原画上传端点 `POST /api/works` 和签名媒体访问 `GET /media/{file}`，其他业务尚未迁移。

## 已实现的最小领域行为

`Tongyin.Domain.Works.Work.Create` 接收已上传原画的标识、所属用户、创建时间和存储路径，
创建处于 `image_pending` 状态的作品；默认没有候选图、预览图和已选图，
且 `ImageAttempts = 0`。

图片生成请求由 `RequestImageGeneration` 登记任务与消耗一次次数；`CompleteImageGeneration` 只接受当前任务返回的单张图片，
将状态改为 `image_ready`，过期任务或重复结果会拒绝，不覆盖现有作品。

失败回调由 `FailImageGeneration` 处理：仅当前待生成任务可转为 `failed` 并记录 `Note` 错误原因；重复或过期回调不覆盖作品。失败后调用 `RequestImageGeneration` 可在额度允许时重新生成，清空旧错误并更新任务 ID；次数用尽则拒绝且不修改已有状态。

确认图片时调用 `SelectImage(0)`，要求作品处于 `ImageReady`，且只能确认单张候选图的第 0 张。成功后记录 `Selected = 0` 并进入 `ModelPending`；失败、待生成、重复确认或传入其他索引均拒绝。此处只实现 Domain 状态转换，不触发外部 3D 服务。

三维模型完成时调用 `CompleteModelGeneration(generationId, modelPath)`：仅已确认图片且仍处于 `ModelPending` 的当前任务可保存 GLB 文件路径并进入 `ModelReady`。旧任务、重复回调、未确认图片和空路径均不能改变作品。文件下载与 GLB 内容校验属于外围适配层，当前未实现。

模型生成失败时调用 `FailModelGeneration(generationId, errorMessage)`：仅当前 `ModelPending` 任务可进入 `Failed` 并记录 `Note`；保留原先确认的单张图片和 `Selected`，拒绝旧任务、重复或迟到失败回调。该阶段暂未实现模型重试入口。

上述初始行为来自原 Go 后端 `backend/work_handlers.go` 的 `createWork`，
其他业务（上传文件、生成任务、持久化、接口、支付等）尚未迁移。

## Application 层第一个用例

`CreateWork.ExecuteAsync(id, owner, createdAt, original)` 调用 `Work.Create` 并通过 `IWorkRepository.AddAsync` 保存作品，保存成功后将相同的作品对象返回给调用方。保存失败时向上抛出错误，不返回假成功。数据库适配器已由 Infrastructure 层实现；用例不负责 HTTP 上传、用户鉴权、作品 ID 或时间生成。

## Infrastructure 层第一个 SQL 适配器

`SqlWorkRepository` 使用 **Dapper 2.1.89** 实现 `IWorkRepository.AddAsync`，通过 `Func<DbConnection>` 获取连接，并使用 `CommandDefinition` 执行参数化 INSERT（支持取消令牌），Domain 和 Application 不依赖 Dapper 或具体数据库驱动。

它写入 Go 后端使用的 `ty_works(id, owner_id, created_at, payload)`，`payload` 保留旧版 JSON 字段名（如 `image_pending`、`image_attempts`），而 Domain 内部继续使用 `WorkStatus` 枚举。表结构由现有数据库管理，适配器不会自动建表，也不会修改旧服务。

基础设施测试使用独立 SQLite 文件模拟同一表结构，覆盖真实插入、重复主键、缺表错误和特殊字符的参数化保存。Dapper 使用 `@参数` 绑定；外围连接工厂可提供 PostgreSQL 或 MySQL 驱动。目前尚未接入 API 入口、数据库连接配置或生产环境，也尚未验证与 Go 服务并发写入的行为。

## 现有小程序登录态兼容

Infrastructure 层增加 `GoSessionTokenValidator`：按照原 Go `backend/auth.go` 的格式校验 `base64url(openid|expires).base64url(HMAC-SHA256(payload))`，使用 `SESSION_SECRET`，核实签名与过期秒数后返回 OpenID。无效签名、过期、空用户及非法令牌均拒绝；不接受空密钥。该能力供后续安全接入 `POST /api/works` 使用，当前尚未新增 HTTP 路由，也未接入原 Go 的用户禁用校验。

## HTTP 创建作品与原画访问

`POST /api/works` 接收微信小程序原有 `Authorization: Bearer` 会话令牌及 `multipart/form-data` 的 `file` 原画字段。服务先验证旧版 Go HMAC 会话，并查询 `ty_users` 拒绝停用或缺失用户，限制原画 10 MiB、接受 JPG/PNG/WebP 文件头。原画保存到 `DATA_DIR/media` 后调用 `CreateWork` 写入 `ty_works`，出错时清理已上传文件；响应为 201，保留旧小程序的 JSON 字段，且不返回用户 OpenID。原画通过带 48 小时签名的 `GET /media/{file}` 地址访问。

启动需要显式配置 `SESSION_SECRET`（至少 24 位）、`DATABASE_URL`，以及 `DB_DRIVER=postgres|mysql`；服务的 `DATA_DIR` 必须与旧 Go 服务一致才能共用上传媒体。已登录的老用户可复用已存在于 `ty_users` 的账号；此阶段不自动创建新的用户档案、不迁移微信登录接口或 AI 生成流程。Go 与 .NET 共用数据库时的并发安全尚未经验证，不应同时作为正式服务提供写操作。

## 验收

需要 .NET 10 SDK：

```powershell
dotnet build backend-dotnet/Tongyin.slnx -c Release
dotnet test backend-dotnet/Tongyin.slnx -c Release
```

仅检查骨架结构：

```powershell
cd backend-dotnet
python -m unittest discover -s tests -p "test_*.py" -v
```

新增功能遵守同目录 `AGENTS.md`：以调用方的业务需求写测试，记录红灯，再实现最小代码并验证绿灯。
一次只完成一个确认过的业务用例，后续迁移需另行确认。

**此分支不替代已运行的 Go 服务。** 不允许把尚未迁移的代码当成完整后端，也不应直接合并上线。
