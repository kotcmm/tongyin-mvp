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

上述初始行为来自原 Go 后端 `backend/work_handlers.go` 的 `createWork`；目前已迁移原画上传、作品创建和 SQL 持久化，后续图片生成任务、模型业务、支付等尚未迁移。

## 图片生成请求的领域准入规则

对照原 Go `startImageGeneration`，`Work.RequestImageGeneration` 只允许首次生成、图片已就绪但未选图后的重新生成，以及图片阶段失败后的重新生成；图片任务正在执行、生成次数耗尽、作品进入模型阶段（包括模型生成失败或完成）均必须拒绝，不更改已有图片、模型和任务记录。该规则仅在 Domain 层实施和测试，**尚未迁移 `POST /api/works/{id}/generate`**；原因是 .NET 侧仍缺少可持久化的任务登记、原子更新、服务商及提示词配置、异步派发和结果处理。不能在未实际派发任务时对小程序返回“AI 已提交”的成功响应。

## Application 层第一个用例

`CreateWork.ExecuteFromImageAsync(owner, image, length)` 通过应用层 `IOriginalImageStore` 端口保存原画，生成作品 ID 和创建时间，然后调用既有 `ExecuteAsync(id, owner, createdAt, original)` 持久化作品。**只有作品保存失败时才回收本次原画**，成功保存后 HTTP 响应签名或格式化错误不会触发删除。Controller 仍负责 multipart 请求格式和响应，应用层不依赖 ASP.NET Core。`OriginalImageLimits.MaxBytes`（10 MiB）由 API 和存储适配器共同引用。

## Infrastructure 层第一个 SQL 适配器

`SqlWorkRepository` 使用 **Dapper 2.1.89** 实现 `IWorkRepository.AddAsync`，通过 `Func<DbConnection>` 获取连接，并使用 `CommandDefinition` 执行参数化 INSERT（支持取消令牌），Domain 和 Application 不依赖 Dapper 或具体数据库驱动。

它写入 Go 后端使用的 `ty_works(id, owner_id, created_at, payload)`，`payload` 保留旧版 JSON 字段名（如 `image_pending`、`image_attempts`），而 Domain 内部继续使用 `WorkStatus` 枚举。表结构由现有数据库管理，适配器不会自动建表，也不会修改旧服务。

基础设施测试使用独立 SQLite 文件模拟同一表结构，覆盖真实插入、重复主键、缺表错误和特殊字符的参数化保存。Dapper 使用 `@参数` 绑定；通过 API 组合根接入 PostgreSQL 或 MySQL 连接工厂。尚未接入生产环境，也尚未验证与 Go 服务并发写入的行为。

## 现有小程序登录态兼容

Infrastructure 层增加 `GoSessionTokenValidator`：按照原 Go `backend/auth.go` 的格式校验 `base64url(openid|expires).base64url(HMAC-SHA256(payload))`，使用 `SESSION_SECRET`，核实签名与过期秒数后返回 OpenID。无效签名、过期、空用户及非法令牌均拒绝；不接受空密钥。该验证器由 `WechatSessionAuthenticationHandler` 调用；`EnabledWechatUserAuthorizationHandler` 通过 Dapper 查询 `ty_users`，对需要账号状态验证的端点统一授权。

## HTTP 身份认证与授权

业务 Controller 通过 `User.GetWechatOpenId()` 统一读取当前微信用户 OpenID，不再反复调用 `FindFirst(ClaimTypes.NameIdentifier)`，也不需要在 Controller 中注入 `IHttpContextAccessor`。该扩展方法只接受认证方案为 `WechatSession` 且具有非空 OpenID 的身份，不能将管理员或支付宝的 `NameIdentifier` 当成微信身份。未来如果需要在非 Controller 服务中获取当前用户，再按实际用例引入独立的请求身份服务。



`WechatSessionAuthenticationHandler` 使用 ASP.NET Core 的 `AddAuthentication` 验证原 Go 的 Bearer 令牌，写入 `HttpContext.User` 中的 `ClaimTypes.NameIdentifier`（微信 OpenID）。**微信小程序与管理后台是两套独立身份体系**：小程序通过 `WechatSessionAuthenticationHandler`（独立认证方案 `WechatSession`）和具名的 `AuthorizationPolicies.Wechat` 策略进行认证授权。该策略要求微信用户已登录，并通过 `EnabledWechatUserRequirement` 校验 `ty_users` 中账号存在且未停用。小程序业务 Controller 使用 `[WechatAuthorize]`，不在控制器动作内解析 Token。**`[WechatAuthorize]` 只是对 `AuthorizationPolicies.Wechat` 的强类型封装，不会创建新的 Token 处理逻辑**。**不把微信认证设置为全局默认授权策略**，避免未来后台登录错误地套用微信账号检查。后台的认证方案、登录接口及管理员专属授权策略须单独实现；目前尚未迁移后台登录。未认证返回 401，令牌有效但无权访问返回 403；签名媒体链接 `GET /media/{file}` 不启用此策略，继续由媒体签名保护。

## API 层 Controller 约定

API 层代码分类：`Controllers/` 放置业务控制器，不为微信用户额外创建 `Customer` 目录；`ViewModels/Works/WorkResponse.cs` 放置面向小程序的响应 DTO（数据传输对象），避免将响应模型与领域实体混放；`Auth/WechatAuthorizeAttribute.cs` 封装微信登录的授权特性。将来管理员可以拥有独立 `[AdminAuthorize]` 和认证方案，但此阶段不预先实现。

`WorksController` 采用 C# 主构造函数直接注入 `CreateWork`、`ListWorks`、`LocalMediaStore` 和日志服务，无需重复声明私有字段；上传动作仍保留空文件校验（400）及单文件 10 MiB 校验（413），以保证旧小程序接口语义。内置 `[Required]` 只能检查缺失，不能替代零字节文件判断；框架级 `[RequestSizeLimit]` 是 HTTP 请求限制，不等同单文件容量规则。

现有 HTTP API 统一采用 ASP.NET Core Web API Controller，`Program.cs` 用 `AddControllers()` 和 `MapControllers()` 注册。微信小程序作品接口由 `Controllers/WorksController.cs` 提供，通过 `[WechatAuthorize]` 复用专属认证授权；`Controllers/MediaController.cs` 仅验证媒体 URL 的签名，不强制微信登录。各操作使用 ControllerBase 的 `User`、`Request` 和 `Response` 属性、构造函数依赖注入及取消令牌参数。上传动作通过 `[FromForm] IFormFile? file` 由 MVC 自动绑定文件；Controller 不再手工调用 `Request.ReadFormAsync()`。它只检查空文件和 10 MiB 业务限额，其他格式识别与持久化交给应用服务。保留 `[RequestSizeLimit]` / `[RequestFormLimits]` 约束 HTTP 请求；为维持旧小程序对非 multipart 的 HTTP 400（而非 MVC 默认的 415），配置 `SuppressConsumesConstraintForFormFileParameters = true`。文件解析错误在模型绑定阶段由 MVC 处理，可能返回框架标准错误体；查询动作继续使用模型绑定和 `ActionResult<T>`。今后后台登录应采用独立的 Admin 认证方案及授权策略。

## HTTP 创建作品与原画访问

`POST /api/works` 接收微信小程序原有 `Authorization: Bearer` 会话令牌及 `multipart/form-data` 的 `file` 原画字段。服务通过 ASP.NET Core Authentication/Authorization 管线验证旧版 Go HMAC 会话，再以微信登录专用策略 `AuthorizationPolicies.Wechat` 查询 `ty_users` 拒绝停用或缺失用户；`WorksController.CreateAsync` 从已认证的 `User` 读取当前 OpenID，限制原画 10 MiB、接受 JPG/PNG/WebP 文件头。Controller 将原画流交给 `CreateWork.ExecuteFromImageAsync`，由应用用例协调 `DATA_DIR/media` 文件保存和 `ty_works` 持久化，只有作品保存失败才回收原画；响应为 201，保留旧小程序的 JSON 字段，且不返回用户 OpenID。原画通过带 48 小时签名的 `GET /media/{file}` 地址访问。

启动需要显式配置 `SESSION_SECRET`（至少 24 位）、`DB_CONNECTION_STRING`，以及 `DB_DRIVER=postgres|mysql`。数据库连接字符串采用 .NET ADO.NET 驱动格式，不能直接使用 Go `DATABASE_URL` 的 URL/DSN。示例：

```env
DB_DRIVER=postgres
DB_CONNECTION_STRING=Host=127.0.0.1;Port=5432;Database=tongyin;Username=tongyin;Password=YOUR_PASSWORD
```

或：

```env
DB_DRIVER=mysql
DB_CONNECTION_STRING=Server=127.0.0.1;Port=3306;Database=tongyin;User ID=tongyin;Password=YOUR_PASSWORD
```

`DB_CONNECTION_STRING` 仅供 .NET 使用，原 Go 服务的 `DATABASE_URL` 保持不变。服务的 `DATA_DIR` 必须与旧 Go 服务一致才能共用上传媒体。已登录的老用户可复用已存在于 `ty_users` 的账号；此阶段不自动创建新的用户档案、不迁移微信登录接口或 AI 生成流程。Go 与 .NET 共用数据库时的并发安全尚未经验证，不应同时作为正式服务提供写操作。

## 我的作品列表查询

`GET /api/works` 沿用旧 Go 接口，使用 `AuthorizationPolicies.Wechat` 校验当前微信用户，返回 JSON 数组（无作品为 `[]`），而不是分页包装对象。Application 层的 `ListWorks` 查询用例经由 `IWorkRepository.ListByOwnerAsync` 获取 `WorkSnapshot`，Infrastructure 用 Dapper 按 `ty_works.owner_id` 筛选，保留既有 JSON 的生成状态、候选图、预览图、模型及 AI 任务元数据。API 统一通过 `WorkResponse.FromSnapshot` 为每次查询重新生成 48 小时有效的媒体 URL，不将原始 OpenID 返回给小程序。查询不会更改数据库内容，也不依赖文件当前存在与否。

## 我的作品详情查询

`GET /api/works/{id}` 沿用原 Go 协议，使用 `[WechatAuthorize]` 检查微信登录状态和账号是否启用。Controller 通过 `GetWork` 用例读取作品；`SqlWorkRepository.GetByIdForOwnerAsync` 使用参数化 `WHERE id = @Id AND owner_id = @Owner` 同时核对作品标识与所属人。作品不存在或属于其他用户时均返回 `404 {"error":"作品不存在"}`，避免暴露其他人的作品是否存在；匿名返回 401，账号停用返回 403。正常返回现有 `WorkResponse` 单个 JSON 对象，重新签发所有原画、候选图、预览和模型的媒体 URL。

## 图片生成任务原子登记（尚未开放 HTTP 生成接口）

Application 层的 `StartImageGeneration.ExecuteAsync(owner, id, freeImageAttempts)` 生成独立任务标识，交由 `IWorkRepository.RegisterImageGenerationAsync` 原子登记，不直接发送 AI 请求。Dapper 适配器在事务内更新 Go 已有的 `ty_store_lock(id=1)` 行以获取排他写锁，与 Go 的 `SELECT ... FOR UPDATE` 竞争同一行锁，然后以作品 ID 和 owner_id 双重筛选目标记录，恢复 Domain `Work` 并调用既有 `RequestImageGeneration` 进行状态与免费次数校验；通过后原子写入新的 `image_pending`、`image_attempts` 和 `generation_id`，清理过期候选图片/预览/模型字段，其他 Go 兼容 JSON 属性保留。失败时事务回滚，不额外扣次数。

沿用 Go 的 `ty_works.payload TEXT` 和 `ty_store_lock` 数据库结构，不增加数据库表，也不让 Controller 直接访问 Dapper。仅解决任务登记与同一作品并发提交；尚无队列、AI 服务商调用、回调处理和故障恢复，因此不会新增 `POST /api/works/{id}/generate`，不能将“登记成功”误报为“AI 已生成或已提交”。

## Go API Key 加密格式兼容

`GoAiKeyDecryptor` 位于 Infrastructure 层，直接解密原 Go 运营后台 `aiEncrypt` 写入 `api_key_cipher` 的数据。密钥是 `SHA-256(UTF8("tongyin-ai-api-key-v1:" + SESSION_SECRET))`，密文格式为 `nonce(12B) | ciphertext | GCM tag(16B)`，采用标准 Base64 **不带等号填充**存储。使用 .NET `AesGcm` 完整验证认证标签，密钥错误、密文损坏等均拒绝且不打印 Key 或密文。解密后的 API Key 仅供后续基础设施适配器发送授权请求使用，不能返回给小程序或写入普通日志。

通过 Go 标准库用固定 nonce 生成跨语言测试密文，测试含中文 UTF-8 及篡改检测。配置读取器仍只读取密文，解密器由 API 组合根注册供下一阶段真实执行适配器使用。**本阶段没有发起或计费任何真实模型请求，也没有开放生成接口。**

## AI 服务商及提示词配置读取

.NET 的 `IAiGenerationSettingsReader`（Application 端口）由 `SqlAiGenerationSettingsReader`（Infrastructure 的 Dapper 实现）提供，通过相同的连接工厂读取旧 Go 后台的 `ty_ai_providers`、`ty_ai_prompts` 两张表。按 `image` / `model` 阶段读取已启用的默认服务商（`enabled=true` 且 `default=true`），以及管理员保存的 `template` 和 `version`。当没有自定义提示词时，回退到 Go `defaultPrompt(stage)` 相同的内置提示词和版本 1；未配置默认服务商时 `Provider` 为空，不伪造可执行的 AI 模型。

`AiProviderSettings.ApiKeyCipher` 仅保留 Go 的 `api_key_cipher` 加密文本，**此阶段不解密、不记录密钥、不向小程序发送密钥**。读取器通过依赖注入注册，但没有加入微信业务 Controller，也没有启动外部 HTTP 请求；前一步的 `StartImageGeneration` 仍然只登记数据库中的待执行任务。后续步骤需先实现 Go 兼容的密钥解密和安全的真实适配器调用，再实现可靠结果回写、失败处理及任务恢复，最后才可开放 `POST /api/works/{id}/generate`。

## Seedream 单张图片生成 HTTP 适配器

新增 `SeedreamImageGenerator`（Infrastructure），输入为 `AiGenerationSettings` 与原画字节；仅允许已启用的默认 `seedream` 图片服务商和方舟官方 HTTPS 图片生成地址。通过 Go 兼容解密器读取服务商 API Key，原画按 PNG/JPEG/WebP 文件头识别并限制为 10 MiB。请求复用 Go 的协议：`model`、`prompt`、原画 `data:image/...;base64,...`、`size=2K`、`response_format=url`、`watermark=false`，不传本地 localhost 地址或多图参数。只接受 HTTP 200、恰好一张图片及有效 HTTPS 结果 URL。

生产通过 `AddHttpClient<SeedreamImageGenerator>` 注册，超时 190 秒、禁止自动重定向；网络错误不自动重试，以免重复计费。错误响应只提取安全格式的错误代码与 HTTP 状态，不回显未经处理的服务商正文、API Key、签名 URL 或 Base64 图片。基础设施测试全部由模拟 HTTP 处理器截获，不进行真实付费 API 请求。

**当前只提供并注册网络调用适配器；不自动派发已登记的任务、不下载并保存生成文件、不回写作品结果，也不开放 `POST /api/works/{id}/generate`。** 必须先完成结果安全下载和状态回写，再将任务登记、派发、成功失败与超时恢复串成可靠的执行流程。

## AI 生成图片的安全下载与本地保存

`GeneratedImageDownloader.DownloadAsync(imageUrl)` 仅接受公开 DNS 域名的 HTTPS 结果地址（标准 443 端口，不含账号信息/锚点），使用 60 秒超时的专用 `HttpClient`；禁止自动重定向、代理及对内网地址的连接。通过 `SocketsHttpHandler.ConnectCallback` 在每次实际建立 TCP 连接时解析 DNS，并过滤回环、RFC1918、链路本地、云元数据等非公网目标，避免仅校验 URL 后遭遇 DNS 重绑定。即使上游未声明 Content-Length，也按流式实际接收字节数严格限制单张图片 **10 MiB**。

成功下载的内容重新交给现有 `LocalMediaStore.SaveImageAsync`，按真实图片头识别 PNG/JPEG/WebP，生成随机本地文件名并保存到共享 `DATA_DIR/media`；只返回本地文件名，不保留临时外链。非 200、空文件、非图片和超大文件均拒绝，不向外暴露带签名的下载地址；文件保存失败依赖原媒体存储逻辑清理部分文件。下载网络策略主要通过本地单元测试验证，公网 DNS、IPv4/IPv6 网络和代理环境仍需部署环境集成测试。

**本步骤没有启动任务调度，也不将结果写入 `ty_works`，不开放 `POST /api/works/{id}/generate`。** 下一步需要保证下载后的文件仅在对应 `generation_id` 仍有效时写入作品，过期结果要清理文件；之后才可完整衔接 AI 派发和失败处理。

## 图片生成结果原子回写与失败清理

Application 新增 `SaveImageGenerationResult.ExecuteAsync(workId, generationId, resultUrl)`，通过 `IGeneratedImageStore` 下载图片后，调用 `IWorkRepository.CompleteImageGenerationAsync` 持久化单张图片结果。生成图片存储端口由现有 `GeneratedImageDownloader` 实现，并复用 `LocalMediaStore.Delete` 清理回写失败的文件，不增加重复的文件存储实现。

SQL 仓储使用与 Go 共用的 `ty_store_lock` 事务锁；在锁内读取作品 JSON，恢复领域 `Work`，调用 `CompleteImageGeneration(generationId, fileName)` 拒绝旧任务、重复回调及非 `image_pending` 状态。有效结果将 `candidates` 写为单个本地文件名并将 `status` 更新为 `image_ready`；不覆盖原画、次数、任务编号及其他 AI 元数据。回写失败时 Application 回收本次下载文件；下载本身失败不会访问数据库。

此步骤尚未实现任务执行调度、Worker 回调、图片失败状态回写或超时恢复，也**不会对小程序开放 `POST /api/works/{id}/generate`**。事务提交出现网络断开等不确定结果时，文件清理仍存在与最终数据库提交状态不一致的理论风险；正式部署前需要进一步设计故障后核对与孤儿文件巡检机制，避免把当前实现当作完整的分布式事务。

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
