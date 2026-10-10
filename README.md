# 童印 · 可运行 MVP（Go API + 微信小程序）

> 目标：儿童画上传 → 1 张形象图 → 确认或免费重试 → 3D 白模预览 → 创建订单 → 微信支付 → 后台打印/发货。

## 当前实际完成情况

- ✅ Go API：微信 code2session 登录、签名会话、作品上传、候选图选择、模型及预览文件、订单创建/查询。
- ✅ Web 管理后台：第1步查看原画 → 第2步上传1张形象图 → 第3步上传 GLB 和白模预览 → 第4步处理已付款订单；不再混合下拉上传。
- ✅ 微信小程序源码：首页、上传、单图确认或重生成、我的作品进度、白模预览、订单列表、订单详情。
- ✅ 微信支付 API v3 代码：JSAPI 预支付、调起小程序支付所需参数、支付通知的验签/解密与订单更新。
- ✅ Go 端到端自动化测试：作品、选图、模型、创建订单及未付款防止制作。
- ⚠️ AI 供应商尚未指定：可先用管理员上传结果，或设置 `AI_WORKER_URL` 接现成 AI worker；不是已接通的真实 AI 自动生成。
- ⚠️ 微信支付尚未接入真实商户密钥，实际付款未测试；必须有真实 AppID、商户号和平台验签材料才能联调。
- ✅ Go 数据库适配层：PostgreSQL / MySQL 可由环境变量选择；文件模式仅限本地开发。数据库表自动初始化（见两份 SQL 脚本）。
- ⚠️ 当前运行环境无法下载 Go 数据库驱动或连接你的 PostgreSQL/MySQL，SQL 版尚未完成实际连接测试；需在可访问数据库的机器运行测试。
- ⚠️ 尚未在微信开发者工具打开、真机预览或提交微信审核；这些需真实 AppID 和部署信息。

## 本地启动（默认 PostgreSQL）

Windows PowerShell：在项目根目录执行 `Copy-Item backend/.env.example backend/.env`，编辑 `.env` 的数据库和密钥配置后执行 `./scripts/dev.ps1`。脚本会加载环境变量并启动 Go，不需手工逐项设置。

```bash
cd backend
cp .env.example .env
# 修改 ADMIN_TOKEN、SESSION_SECRET（长度至少24）、数据库连接密码
# 首次拉取 Go 数据库驱动需要可访问 Go Module 代理
go mod tidy
set -a; source .env; set +a
go run .
```

健康检查：`curl http://localhost:8080/health`。数据库 `tongyin` 需预先创建并给账号授予建表权限；Go 启动时自动创建三张 `ty_*` 表及索引。

不想先启动数据库时，仅限本地调试：把 `DB_DRIVER=file`、`DEV_MODE=true` 写入 `.env`，会使用 `DATA_DIR/store.json`。

运营后台：`http://localhost:8080/admin/`，输入 `.env` 中的 `ADMIN_TOKEN`。

小程序：使用微信开发者工具导入 `miniprogram/`，将 `miniprogram/config.js` 的 `API_BASE` 改为本地地址或 HTTPS API 域名。在开发者工具的本地调试中可关闭域名校验（**仅用于本地调试**）；开发模式使用 `DEV_LOGIN: true` 并确保后端 `DEV_MODE=true`。正式环境必须双端禁用 DEV 模式。

## 运营后台如何用来快速跑通一单

1. 小程序上传画作，点“开始 AI 变身”。
2. 后台 `/admin/` 的「第 2 步」为当前作品上传 **1 张** AI 形象图，立即变为 `image_ready`。
3. 小程序显示唯一一张形象图；满意则确认进入 `model_pending`，不满意可点击重新生成。
4. 后台为该作品上传一个 **GLB** 模型（作为生产模型参考文件）和至少一张白模渲染预览图，自动变成 `model_ready`。
5. 小程序输入收件人信息，创建 `awaiting_payment` 订单。
6. 商户配置完成后调起微信支付；支付回调验签通过后变成 `paid`。
7. 后台将订单变成 `printing` → `shipped`（须填快递单号）→ `completed`。

后台**不允许**把未付款的订单改为制作中。

## AI worker 对接协议（可以选任何 AI 服务商）

设置：`AI_WORKER_URL=https://...`、`AI_WORKER_TOKEN=...`、`AI_WEBHOOK_TOKEN=...`。

Go 在用户提交生成任务时向 `AI_WORKER_URL` 发送：

```json
{
  "work_id": "作品ID",
  "stage": "image",
  "generation_id": "后端为本次生成分配的版本ID",
  "source_url": "有时效的图片HTTPS地址",
  "callback_url": "https://你的API域名/api/internal/ai-result",
  "instructions": "..."
}
```

请求头：`Authorization: Bearer <AI_WORKER_TOKEN>`。Worker 应迅速返回 HTTP 2xx，异步处理；处理好后向 `callback_url` 发送：

```json
{
  "work_id": "作品ID",
  "stage": "image",
  "generation_id": "原请求中的版本ID",
  "urls": ["https://.../candidate.png"]
}
```

**免费重试规则：** 默认 `FREE_IMAGE_ATTEMPTS=2`，即首次生成 1 次，加一次免费重生成机会；后端限制频繁重复请求。付费或观看广告换次数只是后续扩展方向，当前尚未开发，不会假装已扣费或已观看广告。旧任务结果回调必须包含匹配的 `generation_id`，否则会被拒绝，避免覆盖新结果。

3D 阶段：`stage:"model"`、`urls` 为 1–4 张真实白模多角度渲染图地址（不是生成多个候选形象）、`model_url` 是真正的 HTTPS GLB 下载链接。回调请求头必须带 `X-AI-Token: <AI_WEBHOOK_TOKEN>`。后端会下载并保存生成文件，不依赖上游短期 URL。失败可回调 `{ "work_id": "...", "stage": "image", "error": "原因" }`。

注意：这个是你自己的 worker 接口协议，并非某个 AI 厂商的现成 API；确定厂商后，需要开发其适配器。可以用朋友现有的图片/3D 工作流做第一版 AI worker。

## 微信支付配置

后端环境变量：

- `WX_APP_ID`、`WX_APP_SECRET`：小程序主体。
- `WX_MCH_ID`、`WX_MCH_SERIAL`：微信支付普通商户号与商户证书序列号。
- `WX_MCH_PRIVATE_KEY`：商户 RSA 私钥在服务器上的**绝对路径**。
- `WX_PLATFORM_PUBLIC_KEY`：微信支付平台的 RSA 公钥 PEM 或平台证书 PEM 的路径。
- `WX_PLATFORM_SERIAL`：对应平台密钥/证书序列号，开启校验。
- `WX_PAY_API_V3_KEY`：32 字节 APIv3 密钥。
- `WX_PAY_NOTIFY_URL`：HTTPS `https://api.example.com/api/pay/notify`。

**关键**：后台不得把 `wx.requestPayment` 返回成功当成订单已付；仅验签通过的服务端通知变更订单状态。商户密钥不要放入微信小程序或 Git 仓库。微信支付 API 接入与回调尚需在真实商户环境联调。

## PostgreSQL / MySQL 切换

业务代码和 API 不变，只修改 `backend/.env`：

```bash
# 本地 PostgreSQL
DB_DRIVER=postgres
DATABASE_URL=postgres://tongyin:密码@127.0.0.1:5432/tongyin?sslmode=disable

# 线上 MySQL 8+（Docker 内不能用 127.0.0.1 连接宿主机数据库，可使用 host.docker.internal 或数据库所在内网地址）
DB_DRIVER=mysql
DATABASE_URL=tongyin:密码@tcp(db-host:3306)/tongyin?charset=utf8mb4&parseTime=true
```

- PostgreSQL 和 MySQL 都会自动初始化相同逻辑结构的数据库表；不依赖 ORM，也不改小程序代码。
- 代码使用同一套 Go `Repository` 接口。当前为 MVP 采用 SQL 行存 JSON 文档 + 按 owner_id 建索引，后续可继续扩展结构化字段。
- **切换配置不会自动迁移历史数据**。从 PostgreSQL 改 MySQL 前须导出并导入 `ty_works` / `ty_orders` 数据，还要复制 `DATA_DIR/media` 文件（如服务器也同时变更）。两端表结构分别见 `migrations/001_postgres.sql`、`migrations/001_mysql.sql`。
- 当前使用一次性数据库行锁保证订单状态写入的事务一致性，适合早期小规模 MVP；订单量增大后再重构为细粒度 SQL。

## SQL 数据库实际连接测试

```bash
cd backend
# PostgreSQL，凭证从本地环境提供，勿提交
TEST_DB_DRIVER=postgres TEST_DATABASE_URL="postgres://..." go test -run TestSQLRepositoryRoundTrip -v
# MySQL（在有 MySQL 的环境做同样的验证）
TEST_DB_DRIVER=mysql TEST_DATABASE_URL="user:pass@tcp(host:3306)/tongyin?charset=utf8mb4&parseTime=true" go test -run TestSQLRepositoryRoundTrip -v
```

## 服务器部署

```bash
cp backend/.env.example backend/.env
# 设定生产域名 HTTPS PUBLIC_URL、微信 AppID、SESSION_SECRET、ADMIN_TOKEN
# 设定 DEV_MODE=false；各商户密钥配置为 /secrets/ 下只读挂载路径
mkdir -p runtime-data secrets
docker compose up -d --build
```

现有 Nginx/HTTPS 反向代理指向 `127.0.0.1:8080`，见 `nginx.example.conf`。小程序后台需配置 `request` 和 `uploadFile` 合法服务器域名。浏览器通过 `https://api.example.com/admin/` 访问后台。

> 当前项目是**可本地运行的业务原型**，不是已上线的正式收款系统。上线前最少要完成：接已有数据库、对接 AI 提供商/人工生产流程、设置生产 HTTPS 与真实 AppID、实测微信支付回调和订单、配置访问控制/备份、完成小程序资质及隐私披露，并在开发者工具真机验收。

## 接下来需要你提供什么（不需要在聊天里发密码）

1. 数据库连接地址（本地 PostgreSQL，或服务器最终选择 MySQL/PostgreSQL）；密码只放在你本地 `.env`。
2. 对外 API 域名和服务器部署方式（Docker / 直接运行 Go），域名要有 HTTPS。
3. 小程序 AppID、微信支付商户权限是否已开通（敏感密钥留在服务器）。
4. AI 服务商或朋友使用的图生图/图生 3D 服务；暂时没有也能先走人工补齐结果。
