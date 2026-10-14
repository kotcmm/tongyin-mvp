# 童印 ASP.NET Core 10 后端迁移（进行中）

## 目标与强约束

这是 **tongyin-mvp 原仓库内部的并行迁移**，不是重新开发一个脱离业务的系统。

- 完整迁移 Go 现有能力，并且**验证全部通过后才删除 `backend/` Go 代码**。
- 原生微信小程序、管理后台现有页面与 HTTP 路由 / JSON 响应格式应保持不变。
- 数据持久化继续使用既有 `ty_works`、`ty_orders`、`ty_addresses`、`ty_users`、`ty_admins`、`ty_audit`、`ty_ai_providers`、`ty_ai_prompts` 表，`payload` JSON 及用户 Owner/OpenID 不变。
- 保持 Go 管理员密码 PBKDF2 校验、管理员 token、客户 token、媒体 URL 签名和 AI Key 加密方案兼容。
- **不能在同一数据库/数据文件上同时运行 Go 与 .NET 测试写入**。正式切换前创建数据库副本测试。
- 未迁移的接口不得静默代理 Go，也不得宣称可上线。

## 当前迁移状态

**此分支用于并行验收**，不能直接替换生产 Go 服务。所有现有 Go 业务路由已经有对应 .NET 路由，但支付、AI、物流等行为仍需使用真实服务验证。

| 模块 | C# 迁移状态 | 下阶段验收 |
|---|---|---|
| ASP.NET Core 10 启动、静态后台资源 | 已建立，补充 ES Modules MIME 与静态资源回归用例 | 后台与 3D 页面实机打开 |
| File / PostgreSQL / MySQL 快照存储 | 已编写兼容层 | 与真实 PostgreSQL / MySQL 副本读写对照 |
| 密码、签名、客户/管理员令牌 | 已编写 | 跨语言黄金数据与真实旧账号验证 |
| 微信登录 /api/auth/wechat、/api/me | 初版 | 真机登录与 disabled 账号 |
| 原画上传 /api/works、作品列表详情 | 初版 | 手机上传、多用户隔离、媒体签名 |
| 收货地址、创建订单、订单列表、修改未付订单 | 初版 | 和 Go 状态、字段、错误码对照 |
| 后台管理员登录、看板、搜索、用户停用、审计读取 | 按 Go 权限表及分页规则修复，增加权限测试 | 角色权限、数据分页与后台页面 |
| 图片 / 3D 自动生成、AI 配置及回调 | 初版 API 全部移植 | Seedream 真机生成/异步 Worker、并发状态及额度 |
| 后台步骤式模型上传及状态操作 | 初版 API 全部移植 | GLB 上传、状态防越级及运营人员权限 |
| 微信支付 JSAPI / 回调验签解密 | 初版 API 已移植、未实测 | 微信沙箱 / 测试商户 & 幂等、证书和签名兼容 |
| 快递100与物流 / 发货 / 退款 | 初版 API 已移植、未实测 | 真实平台返回、权限与状态、轨迹刷新限频 |
| 完整接口契约与端到端回归 | **待完成** | 同样请求两套后端相同结果 |
| Go 移除 | **等待用户验收，禁止提前删除** | 全模块达标并获得确认 |

## Windows 本地验收（先测，暂不切换 Go）

从 GitHub 切到 `feat/dotnet-backend-migration` 分支后，安装 .NET SDK 10，在 Windows 命令行进入 `backend-dotnet` 运行：

```cmd
run-local.cmd
```

该脚本使用 `http://localhost:18081` 和独立目录 `backend-dotnet/data/`。**不会读取或覆盖你当前 Go 后端的数据目录和 PostgreSQL 数据库**。首次启动生成随机开发管理员密码并在终端显示一次，请保存。后台网址：`http://localhost:18081/admin/`。停止按 Ctrl+C。

先运行自动化检查（从 `backend-dotnet` 目录）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\\verify-local.ps1
```

检查完成后逐步人工验证：登录后台 → 新建儿童画作品 → 人工上传一张形象图 → 用户确认 → 上传 GLB → 订单提交/取消 → 管理后台查询/角色权限。AI、微信支付、物流真实服务待配置单独测试，**不要在未确认回调和收费风险前使用正式商户下单或反复生图**。

测试现有数据库时须复制一份 PostgreSQL 数据库和媒体目录，填写与 Go **同一个** `SESSION_SECRET`，配置 `DB_DRIVER=postgres`、克隆库 `DATABASE_URL`、`DATA_DIR`、`LISTEN_ADDR=:18081` 后**单独手动运行** `dotnet run --project src/Tongyin.Api`。不要用以上 `run-local.cmd` 去连接正式数据库，该脚本始终使用隔离的 file 模式。

**停止 Go 后台服务并删除 Go 代码的前提**：迁移分支最新 CI 全绿、现实数据克隆回归、Seedream/3D/支付/物流联调和你的本地验收全部通过。此分支只新增 C#，不会删除 Go。

## 运行（仅用于独立测试）

需要 .NET SDK 10。在 `backend-dotnet` 目录：

```powershell
$env:DEV_MODE="true"
$env:DB_DRIVER="file"
$env:DATA_DIR="./data"
$env:LISTEN_ADDR=":18081"
$env:PUBLIC_URL="http://localhost:18081"
$env:SESSION_SECRET="CHANGE_TO_AT_LEAST_24_CHAR_RANDOM_SECRET"
$env:ADMIN_BOOTSTRAP_PASSWORD="CHANGE_TO_STRONG_PASSWORD"
dotnet run --project src/Tongyin.Api
```

初版监听 **18081**，默认不占用 Go 服务的 18080。避免指向现有 Go 数据目录。正式跨数据库验证应使用克隆的数据与单独端口，并保管好 SESSION_SECRET（更改会使原有 AI Key 无法解密）。

**不要将真实密钥写入 appsettings、源码、工作流或测试文件。** 后续迁移通过后会补齐发布部署文件并替换 Go 启动入口。


## API 路由覆盖检查

`node scripts/check-dotnet-api-surface.cjs` 对比 Go 与 C# 的 API **HTTP method + path**，确保新代码未漏掉任何业务入口。**这不是行为等价的证明**：完整业务验收还需在副本数据上测试每个模块、错误状态、越权防护、支付通知和 AI 上游通信。
