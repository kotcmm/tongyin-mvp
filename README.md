# 童印（Tongyin）

儿童画定制 3D 打印微信小程序 MVP。

**唯一后端：.NET 10 / ASP.NET Core**。前端为微信原生小程序，管理后台静态页面由 .NET API 同站点提供。**不再使用 Go 后端、Go 管理后台或 Go Docker 容器。**

## 目录

| 路径 | 用途 |
| --- | --- |
| `backend-dotnet/src/Tongyin.Api/` | API、身份认证、管理页面及后台任务 |
| `backend-dotnet/src/Tongyin.Application/` | 业务用例和接口边界 |
| `backend-dotnet/src/Tongyin.Domain/` | 作品、订单领域规则 |
| `backend-dotnet/src/Tongyin.Infrastructure/` | Dapper、PostgreSQL/MySQL、媒体和 AI/微信接口适配 |
| `backend-dotnet/tests/` | xUnit 自动化测试 |
| `backend-dotnet/sql/` | 数据库初始化和旧管理员表清理脚本 |
| `miniprogram/` | 微信原生小程序、必要的 3D 渲染依赖 |
| `scripts/check-miniapp-*.cjs` | 小程序静态与行为检查 |
| `.github/workflows/` | .NET 和微信小程序两套 CI |

## 本地启动（Visual Studio）

1. 从 `feat/dotnet-clean-skeleton` 分支拉取代码，VS 2026 打开 `backend-dotnet/Tongyin.slnx`，设置 `Tongyin.Api` 为启动项目。
2. 连接现有 PostgreSQL 本地数据库。新数据库按 `backend-dotnet/sql/postgres/` 的 `000_baseline.sql`、`001_system_settings.sql`、`003_identity_admin.sql` 顺序初始化。
3. 在 Visual Studio 的 **管理用户机密** 中配置 `DB_CONNECTION_STRING`、`SESSION_SECRET`、`DATA_DIR`、管理员首次初始化账号。详见 [后端使用说明](backend-dotnet/README.md)。
4. 使用“童印 API · 本地 18080”配置 **F5** 启动；后台访问 `http://localhost:18080/admin/`。
5. 微信开发者工具导入 `miniprogram/`；当前开发态 `API_BASE` 为 `http://localhost:18080`，与本地 API 一致。手机真机不能使用 localhost。

运行全部 .NET 测试：

```powershell
dotnet test backend-dotnet/Tongyin.slnx -c Release -p:NuGetAudit=true -p:WarningsAsErrors=NU1903
```

小程序的代码检查：

```powershell
node scripts/check-miniapp-3d.cjs
node scripts/check-miniapp-identity.cjs
node scripts/check-miniapp-modules.cjs
node scripts/check-order-flow.cjs
node scripts/check-ui-utils.cjs
```

## 数据库清理

**原 Go 账号表 `ty_admins` 和过渡版 `ty_admin_credentials` 已废弃。**现役登录由 ASP.NET Core Identity 的 `ty_admin_identity` 承担。确认已备份、且新的管理员已成功登录后，再审查并手动运行：

`backend-dotnet/sql/postgres/900_drop_legacy_admin_tables.sql`

它不会删除作品、订单、微信用户、媒体元数据或审计记录。**`ty_system_settings` 仍是必需表**：如果在数据库管理工具里没有看到，请运行 `001_system_settings.sql` 补齐，而不是删除相关代码。

## 当前范围

核心流：上传儿童画 → AI 生成单张形象 → 确认 → 生成 GLB → 模型预览 → 下单 / 微信支付 → 运营后台审核、生产与物流。代码和自动化测试通过不等于真实服务商、真实微信支付及真机流程已经验收。

**兼容提示：**部分 .NET 类仍读取旧 Go 数据的 JSON、密文或签名格式（例如 `GoAiKeyDecryptor`），这些是历史数据兼容适配，并不表示还依赖 Go 运行时。

目前仓库不包含生产 Docker 部署配置；原先指向 Go 的 Dockerfile/Compose 已移除，部署 .NET 时应另行验证容器或服务器配置。
