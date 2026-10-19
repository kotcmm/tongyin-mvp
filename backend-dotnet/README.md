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

已存在这些业务表的旧库可重复执行 `CREATE TABLE IF NOT EXISTS` 的初始化脚本，但应先做完整备份；脚本不会修改业务表原有列结构。MySQL 对应文件在 `sql/mysql/`。数据层使用 Dapper，不需要 EF Core 数据库上下文。

**注意：**你的截图中有 `ty_admin_identity`，但没有 `ty_system_settings`；后者仍用于微信系统设置，应执行对应的 `001_system_settings.sql` 补齐。

旧 Go 后台用户表 `ty_admins`、临时自写登录的 `ty_admin_credentials` **不再使用**。确认新 Identity 管理员已初始化，并先执行数据库备份后，可人工执行 `sql/postgres/900_drop_legacy_admin_tables.sql`。脚本会检查管理员是否存在，没有确认就拒绝删除；不使用 CASCADE，不会删除订单、作品、微信用户等表。

## 管理后台登录

首次运行使用本地用户机密中的 `ADMIN_BOOTSTRAP_USER` / `ADMIN_BOOTSTRAP_PASSWORD` 登录。这是**仅用于首次初始化的配置**；新账号写入 `ty_admin_identity` 后，移除 `ADMIN_BOOTSTRAP_PASSWORD`。后续可在“账号设置”修改用户名或密码。

认证方案：ASP.NET Core Identity Core 的 `UserManager`、`SignInManager` 和 Dapper `IUserStore`；密码由标准 Identity PasswordHasher 处理。浏览器使用 `HttpOnly`、`SameSite=Strict` 的登录 Cookie，有效期八小时。写操作要求 `X-CSRF-TOKEN`，账号密码修改及退出会更新 Identity SecurityStamp 使旧会话失效。后台与小程序微信认证、AI 回调认证互相隔离，不再支持 `ADMIN_TOKEN`。

**开发环境允许 HTTP；生产后台必须使用 HTTPS**。生产部署要保留并共享 ASP.NET Core Data Protection 密钥，否则重启或多个副本间切换会导致 Cookie 无效。

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
