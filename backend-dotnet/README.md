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

**`SESSION_SECRET` 是仍在使用的服务端主密钥，不能与已废弃的后台固定管理员令牌混淆。**目前它用于微信小程序会话 HMAC 签名、图片/GLB 媒体链接签名、微信 AppSecret 加解密，以及历史 AI API Key 的解密。后台 Identity 的 Cookie 使用 ASP.NET Core Data Protection，**不使用 `SESSION_SECRET` 进行登录**。删掉 `SESSION_SECRET` 后 API 会拒绝启动；任意替换其值还可能让历史加密配置无法解密，请沿用原值，真正轮换前先做数据重加密迁移。

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

已存在这些业务表的旧库可重复执行 `CREATE TABLE IF NOT EXISTS` 的初始化脚本，但应先做完整备份；脚本不会修改业务表原有列结构。MySQL 对应文件在 `sql/mysql/`。数据层使用 Dapper，不需要 EF Core 数据库上下文。

**注意：**你的截图中有 `ty_admin_identity`，但没有 `ty_system_settings`；后者仍用于微信系统设置，应执行对应的 `001_system_settings.sql` 补齐。

旧 Go 后台用户表 `ty_admins`、临时自写登录的 `ty_admin_credentials` **不再使用**。确认新 Identity 管理员已初始化，并先执行数据库备份后，可人工执行 `sql/postgres/900_drop_legacy_admin_tables.sql`。脚本会检查管理员是否存在，没有确认就拒绝删除；不使用 CASCADE，不会删除订单、作品、微信用户等表。

## 删除作品与媒体回收

`DELETE /api/works/{id}` 会校验作品归属、订单和已认领的生成任务；有订单或任务正在执行时不允许删除。可删除时，在作品数据库删除事务内，将 `original`、`candidates`、`preview`、`model` 里符合安全规则的媒体文件名去重写入 `ty_media_cleanup`。

`MediaCleanupPollingService` 每 10 秒最多处理 16 条任务，使用与作品写入相同的 `ty_store_lock` 数据库锁再次核对当前作品和订单 JSON。文件仍被引用时跳过物理删除；无引用时才删除 `DATA_DIR/media/` 内的本地文件。失败时事务回滚，任务保留供重试；重启可继续处理。不会删除整个 `media` 文件夹、外部 URL 或任意目录路径。

**已在使用的旧数据库必须先执行 `sql/postgres/004_media_cleanup.sql`（MySQL 对应 `sql/mysql/`）**。此脚本仅创建待清理队列表，不会主动删除旧素材。只处理新发生的作品删除；过去遗留的孤儿文件不会被自动批量删除，避免误删迁移时尚未关联的数据。无须改动前端 API。

## 管理后台登录

首次运行使用本地用户机密中的 `ADMIN_BOOTSTRAP_USER` / `ADMIN_BOOTSTRAP_PASSWORD` 登录。这是**仅用于首次初始化的配置**；新账号写入 `ty_admin_identity` 后，移除 `ADMIN_BOOTSTRAP_PASSWORD`。后续可在“账号设置”修改用户名或密码。

认证方案：ASP.NET Core Identity Core 的 `UserManager`、`SignInManager` 和 Dapper `IUserStore`；密码由标准 Identity PasswordHasher 处理。浏览器使用 `HttpOnly`、`SameSite=Strict` 的登录 Cookie，有效期八小时。写操作要求 `X-CSRF-TOKEN`，账号密码修改及退出会更新 Identity SecurityStamp 使旧会话失效。后台与小程序微信认证、AI 回调认证互相隔离，不再支持 `ADMIN_TOKEN`。

**开发环境允许 HTTP；生产后台必须使用 HTTPS**。生产部署要保留并共享 ASP.NET Core Data Protection 密钥，否则重启或多个副本间切换会导致 Cookie 无效。

## 运营后台配置：微信、AI 图片和 3D 模型

左侧菜单现在包括 **微信小程序、AI 图片生成、AI 3D 模型**。

- **微信小程序**：读写 `GET/PUT /api/admin/system/wechat`，保存 AppID 和只写入的 AppSecret；后台不显示已保存密钥，留空表示保留之前的密钥。需要数据库的 `ty_system_settings` 表，对应 `sql/postgres/001_system_settings.sql`。
- **AI 图片生成**：读写 `GET/PUT /api/admin/ai/settings/image`，当前仅支持火山方舟官方 **Seedream** 图片生成接口 `https://ark.cn-beijing.volces.com/api/v3/images/generations`，填写模型 ID、API Key、启用开关和图片提示词。保存后新任务直接使用数据库配置。
- **AI 3D 模型**：读写 `GET/PUT /api/admin/ai/settings/model`，当前适配器是**童印异步 Worker 协议**，需要支持请求 `source_url` / `callback_url` 等字段，并将生成结果通过 `/api/internal/ai-result` 回调。只支持公网标准 HTTPS API；普通第三方 3D API 若协议不同，必须先开发专用适配器，不能仅填写它的地址就认为已接通。还需公网 HTTPS `PUBLIC_URL` 和 Worker 回调凭据。
- **安全与数据库**：两类 AI 配置均保存在 `ty_ai_providers` 和 `ty_ai_prompts`；API Key 以 AES-256-GCM 加密后保存，与此前 Go 数据兼容。读取仅返回 `hasApiKey` 状态，不传输明文或密文；密钥留空不会覆盖旧密钥。保存时写入 `ty_audit` 审计记录，不包含密钥。禁用阶段会阻止新的 AI 任务认领，但不会清空旧结果或重试可能已计费的任务。

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
- `Controllers/Internal/`：AI Worker 回写及微信支付通知
- `Controllers/Shared/`：`/media/{file}` 签名文件下载

`Tongyin.Application/Works/` 按 Core、ImageGeneration、ModelGeneration、Recovery、Administration、Media 分类。真实 AI 网络异常不会自动重试已认领的收费任务；管理后台提供超时任务人工核实与恢复入口。保留 `GoAiKeyDecryptor` 是为了读取既有加密服务商配置，不是依赖 Go 服务。

**尚需真实环境验证：**微信 code2Session、服务商生成结果、GLB 真机渲染、微信支付和物流。项目自动测试成功不代表已经完成实收款验收。

## 测试

```powershell
dotnet test backend-dotnet/Tongyin.slnx -c Release -p:NuGetAudit=true -p:WarningsAsErrors=NU1903
```

.NET GitHub Actions 自动执行全套 Domain、Application、Infrastructure 与 Api 测试。约定见 `backend-dotnet/AGENTS.md`。
