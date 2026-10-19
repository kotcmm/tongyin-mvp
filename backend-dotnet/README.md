# 童印 .NET 10 后端

> 运行服务、支付及 AI 对接的唯一后端工程。采用 Domain / Application / Infrastructure / Api 四层架构。
>
> **请从 Visual Studio 的 `Tongyin.Api` 按 F5 启动；不要使用已经废弃的 Go 运行命令。**

## Visual Studio 本地调试

在仓库根目录：

```powershell
git fetch origin
git switch feat/dotnet-clean-skeleton
git pull --ff-only origin feat/dotnet-clean-skeleton
dotnet test backend-dotnet/Tongyin.slnx -c Release
```

用 VS 2026 打开 `backend-dotnet/Tongyin.slnx`，启动项目设为 `Tongyin.Api`，配置选择“童印 API · 本地 18080”，并在 **Tongyin.Api → 管理用户机密** 中设置：

```json
{
  "DB_CONNECTION_STRING": "Host=localhost;Port=5432;Database=tongyin;Username=postgres;Password=你的本机密码",
  "SESSION_SECRET": "你原来使用的至少24位密钥",
  "DATA_DIR": "E:\\童印\\tongyin-mvp\\backend-dotnet\\data",
  "ADMIN_BOOTSTRAP_USER": "admin",
  "ADMIN_BOOTSTRAP_PASSWORD": "用你自己生成的至少12位强密码"
}
```

**`SESSION_SECRET` 是仍在使用的服务端主密钥，不能与已废弃的后台固定管理员令牌混淆。**目前它用于微信小程序会话 HMAC 签名、图片/GLB 媒体链接签名、微信 AppSecret 加解密，；AI API Key 现已单独使用 ASP.NET Core Data Protection 加密。后台 Identity 的 Cookie 使用 ASP.NET Core Data Protection，**不使用 `SESSION_SECRET` 进行登录**。删掉 `SESSION_SECRET` 后 API 会拒绝启动；任意替换会使现有微信会话和媒体签名失效，请沿用原值。AI API Key 的加解密依赖持久化的 ASP.NET Core Data Protection 密钥环，生产重启/容器更新必须保留密钥环。

初始化管理员不需要配置旧版固定令牌；Visual Studio 用户机密中若仍有 `ADMIN_TOKEN`，可以直接删除，后端不会读取。

`launchSettings.json` 已设置 `DB_DRIVER=postgres`、`DEV_MODE=true`、`PUBLIC_URL=http://localhost:18080`。如使用 MySQL，请把 DB_DRIVER 改为 `mysql`，并使用 MySqlConnector 连接串。**不要把密码和密钥写进 git 跟踪文件。**

本地图片位置为 `backend-dotnet/data/media/` 时，`DATA_DIR` 填其上级 `backend-dotnet/data/`，不要再追加 media。若使用相对路径，请特别注意 VS 的实际工作目录；推荐使用绝对路径。

F5 启动后，后台是 **http://localhost:18080/admin/**。根路径 `/` 返回 404 并不表示服务没有启动。小程序默认使用 `http://localhost:18080`，真机联调必须使用手机能访问、且符合微信要求的地址。

## 数据库表与初始化

PostgreSQL：

| 顺序 | SQL 文件 | 作用 |
| --- | --- | --- |
| 1 | `sql/postgres/000_baseline.sql` | 现役业务表：作品、订单、地址、微信用户、AI 服务商/提示词、审计与事务锁 |
| 2 | `sql/postgres/001_system_settings.sql` | 微信 AppID / AppSecret 等系统配置 |
| 3 | `sql/postgres/003_identity_admin.sql` | ASP.NET Core Identity 管理员账号 |
| 4 | `sql/postgres/004_media_cleanup.sql` | 已删除作品的媒体回收持久化队列 |
| 5 | `sql/postgres/005_model_jobs.sql` | 混元 JobId、供应商状态、提交时加密凭据快照 |
| 可选 | `sql/postgres/006_reset_ai_provider_credentials.sql` | **仅重置旧 AI 服务商配置**，便于按新密钥格式重新录入；谨慎使用 |

已经运行过童印旧版本的数据库，**必须在启动新版 .NET API 前补执行 `005_model_jobs.sql`**；旧版 AI 服务商密钥采用 Go AES 格式，不再可用。先备份数据库，如确认重新录入 Seedream/混元 API Key，可以执行可选的 `006_reset_ai_provider_credentials.sql`，它只删除 `ty_ai_providers`，不会删除作品、订单、已生成图片、GLB 或提示词。**不要在存在未完成的真实付费模型任务时清空模型任务表。**

已存在这些业务表的旧库可重复执行 `CREATE TABLE IF NOT EXISTS` 的初始化脚本，但应先做完整备份；脚本不会修改业务表原有列结构。MySQL 对应文件在 `sql/mysql/`。数据层使用 Dapper，不需要 EF Core 数据库上下文。

**注意：**你的截图中有 `ty_admin_identity`，但没有 `ty_system_settings`；后者仍用于微信系统设置，应执行对应的 `001_system_settings.sql` 补齐。

旧 Go 后台用户表 `ty_admins`、临时自写登录的 `ty_admin_credentials` **不再使用**。确认新 Identity 管理员已初始化，并先执行数据库备份后，可人工执行 `sql/postgres/900_drop_legacy_admin_tables.sql`。脚本会检查管理员是否存在，没有确认就拒绝删除；不使用 CASCADE，不会删除订单、作品、微信用户等表。

## 删除作品与媒体回收

`DELETE /api/works/{id}` 会校验作品归属、订单和已认领的生成任务；有订单或任务正在执行时不允许删除。可删除时，在作品数据库删除事务内，将 `original`、`candidates`、`preview`、`model` 里符合安全规则的媒体文件名去重写入 `ty_media_cleanup`。

`MediaCleanupPollingService` 每 10 秒最多处理 16 条任务，使用与作品写入相同的 `ty_store_lock` 数据库锁再次核对当前作品和订单 JSON。文件仍被引用时跳过物理删除；无引用时才删除 `DATA_DIR/media/` 内的本地文件。失败时事务回滚，任务保留供重试；重启可继续处理。不会删除整个 `media` 文件夹、外部 URL 或任意目录路径。

**已在使用的旧数据库必须先执行 `sql/postgres/004_media_cleanup.sql`（MySQL 对应 `sql/mysql/`）**。此脚本仅创建待清理队列表，不会主动删除旧素材。只处理新发生的作品删除；过去遗留的孤儿文件不会被自动批量删除，避免误删迁移时尚未关联的数据。无须改动前端 API。

## 管理后台登录

首次运行使用本地用户机密中的 `ADMIN_BOOTSTRAP_USER` / `ADMIN_BOOTSTRAP_PASSWORD` 登录。这是**仅用于首次初始化的配置**；新账号写入 `ty_admin_identity` 后，移除 `ADMIN_BOOTSTRAP_PASSWORD`。后续可在“账号设置”修改用户名或密码。

认证方案：ASP.NET Core Identity Core 的 `UserManager`、`SignInManager` 和 Dapper `IUserStore`；密码由标准 Identity PasswordHasher 处理。浏览器使用 `HttpOnly`、`SameSite=Strict` 的登录 Cookie，有效期八小时。写操作要求 `X-CSRF-TOKEN`，账号密码修改及退出会更新 Identity SecurityStamp 使旧会话失效。后台与小程序微信认证互相隔离，不再支持 `ADMIN_TOKEN` 或旧 AI Worker 回调认证。

**开发环境允许 HTTP；生产后台必须使用 HTTPS**。生产部署要保留并共享 ASP.NET Core Data Protection 密钥，否则重启或多个副本间切换会导致 Cookie 无效。

## 后台菜单恢复和 AI 生成任务

运营后台的当前菜单由 URL hash（例如 `/admin/#works`）标识。切换菜单时更新 URL，刷新或重新登录后返回原菜单，而不是跳回“全部订单”。无论选中了作品管理、图片生成任务、3D 模型生成任务还是配置页，都遵循同一规则。

运营任务列表：
- `GET /api/admin/ai/image-tasks`：完整图片生成任务列表
- `GET /api/admin/ai/model-tasks`：完整 3D 模型生成任务列表

两者都需要管理员 Identity 身份，均为只读查询。按行展示每个作品当前阶段的任务，包括状态（`queued` 排队中、`processing` 已认领处理中、`stalled` 超过 15 分钟且仍未完成、`completed` 成功、`failed` 失败）、系统已知的 API 阶段、作品编号、任务编号、阶段服务商标识、提示词版本、创建时间、认领时间及失败备注。页面每 15 秒自动更新，并支持关键词与状态筛选。

**进度真实性**：图片阶段依旧仅能显示原子认领和结果状态。3D 阶段会读 `ty_model_jobs` 的混元官方 `JobId` 与 `WAIT`（云端排队）、`RUN`（云端运行）、`DONE`（结果完成）、`FAIL`（失败）。提交请求但未取得 JobId 的 `submitting` 状态会保留待人工核查，绝不自动重新提交以防重复计费。当前不提供百分比进度，不记录完整 HTTP 请求正文（避免泄漏图像与密钥）。

仅 `can_review=true` 的已认领超时任务可以使用原有人工核实接口 `/api/admin/ai/stalled-image-tasks/{id}/...` 和 `stalled-model-tasks/{id}/...`。禁止对正常排队中、正在正常处理和已完成的任务执行人工成功或失败回写；不自动重新调用可能计费的模型。

## 运营后台配置：微信、AI 图片和 3D 模型

左侧菜单现在包括 **微信小程序、AI 图片生成、AI 3D 模型**。

- **微信小程序**：读写 `GET/PUT /api/admin/system/wechat`，保存 AppID 和只写入的 AppSecret；后台不显示已保存密钥，留空表示保留之前的密钥。需要数据库的 `ty_system_settings` 表，对应 `sql/postgres/001_system_settings.sql`。
- **AI 图片生成**：读写 `GET/PUT /api/admin/ai/settings/image`，当前仅支持火山方舟官方 **Seedream** 图片生成接口 `https://ark.cn-beijing.volces.com/api/v3/images/generations`，填写模型 ID、API Key、启用开关和图片提示词。保存后新任务直接使用数据库配置。
- **AI 3D 模型**：读写 `GET/PUT /api/admin/ai/settings/model`，直接调用腾讯混元专业版 [OpenAI 兼容接口](https://cloud.tencent.com/document/product/1804/126189)，API 固定为 `https://api.ai3d.cloud.tencent.com/v1/ai3d/submit`，模型为 `3.0` 或 `3.1`。卡通图由 .NET 从 `DATA_DIR/media` 读取并编码为 Base64 提交 `GenerateType=Geometry`（白模）。返回 JobId 后每轮使用同一任务查询 `/v1/ai3d/query`；`DONE` 后下载 GLB 到本地。**不需要独立 Worker、AI_WEBHOOK_TOKEN、回调端点，也不要求公网 HTTPS PUBLIC_URL**。但小程序和运营后台的签名媒体地址仍使用 `PUBLIC_URL` 生成。该 API 的图片和 Prompt 参数不能同时提交，因此当前界面里的 3D 工艺备注**不会**随图片提交给混元。
- **安全与数据库**：两类 AI 配置均保存在 `ty_ai_providers` 和 `ty_ai_prompts`；API Key 使用 ASP.NET Core Data Protection 加密后存储，**不再兼容 Go 密文**；旧密钥必须在后台重新填写。读取仅返回 `hasApiKey` 状态，不传输明文或密文；密钥留空不会覆盖旧密钥。保存时写入 `ty_audit` 审计记录，不包含密钥。禁用阶段会阻止新的 AI 任务认领，但不会清空旧结果或重试可能已计费的任务。

后台配置请求使用 Identity Cookie 和 CSRF 安全令牌，只允许已登录管理员操作。检查失败时应先确认数据库存在 `ty_ai_providers`、`ty_ai_prompts`、`ty_audit`、`ty_store_lock` 和 `ty_system_settings`。

### 图片生成：唯一应用入口

`src/Tongyin.Application/Works/ImageGeneration/ImageGenerationService.cs` 是图片生成流程的唯一应用服务：
- `QueueAsync`：小程序发起任务时验证当前服务商并原子登记，不直接调用 AI。
- `ProcessNextAsync`：后台定时器触发，检查配置、读取待处理任务、原子认领、读取原画、调用 Seedream、下载一张结果图并写回作品；同一次处理只读取一次 AI 配置。
- `SeedreamImageGenerator` 是具体 HTTP 适配器；`SqlQueuedImageGenerationReader` 和 `IWorkRepository` 的实现仍负责数据库安全，`GeneratedImageDownloader` 负责防 SSRF 下载。

不再存在 `StartImageGeneration`、`DispatchQueuedImageGeneration`、`ExecuteImageGeneration` 或 `SaveImageGenerationResult` 四层用例包装。任务仍由后端后台服务异步处理，网络错误可能已经计费，因此已认领任务不会自动重试，保留运营核查入口。

## 功能与接口分组

- `Controllers/Miniapp/`：微信登录、用户资料、作品上传、图片确认、GLB、订单及支付
- `Controllers/Admin/`：管理员登录和账号维护、全部作品、订单/发货、AI 异常核查、微信设置
- `Controllers/Internal/`：微信支付通知（无 AI Worker 回调）
- `Controllers/Shared/`：`/media/{file}` 签名文件下载

`Tongyin.Application/Works/` 按 Core、ImageGeneration、ModelGeneration、Recovery、Administration、Media 分类。真实 AI 网络异常不会自动重试已认领的收费任务；管理后台提供超时任务人工核实与恢复入口。旧 `GoAiKeyDecryptor`、Worker 回调服务和 `PUBLIC_URL` 派发限制均已清理。3D 的统一入口是 `ModelGenerationService`，状态存储于 `ty_model_jobs`。

**尚需真实环境验证：**腾讯混元实际扣费请求/响应、GLB 文件（大小/类型/下载时效）、真机 GLB 预览、微信 code2Session、支付和物流。项目自动测试成功不代表已经完成实收款验收。

## 测试

```powershell
dotnet test backend-dotnet/Tongyin.slnx -c Release -p:NuGetAudit=true -p:WarningsAsErrors=NU1903
```

.NET GitHub Actions 自动执行全套 Domain、Application、Infrastructure 与 Api 测试。约定见 `backend-dotnet/AGENTS.md`。
