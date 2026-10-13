# 童印 · 可运行 MVP（Go API + 微信小程序）

> 目标：儿童画上传 → 1 张形象图 → 确认或免费重试 → 3D 白模预览 → 创建订单 → 微信支付 → 后台打印/发货。

## 当前实际完成情况

- ✅ Go API：微信 code2session 登录、签名会话、作品上传、候选图选择、模型及预览文件、订单创建/查询。
- ✅ Web 管理后台：第1步查看原画 → 第2步上传1张形象图 → 第3步上传 GLB（静态预览已改为可选） → 第4步处理已付款订单；不再混合下拉上传。
- ✅ 微信小程序源码：首页、上传、单图确认或重生成、我的作品进度、直接在白模页面旋转、缩放的 GLB 预览、订单列表、订单详情。
- ✅ 微信支付 API v3 代码：JSAPI 预支付、调起小程序支付所需参数、支付通知的验签/解密与订单更新。
- ✅ Go 端到端自动化测试：作品、选图、模型、创建订单及未付款防止制作。
- ⚠️ AI 供应商尚未指定：可先用管理员上传结果，或设置 `AI_WORKER_URL` 接现成 AI worker；不是已接通的真实 AI 自动生成。
- ⚠️ 微信支付尚未接入真实商户密钥，实际付款未测试；必须有真实 AppID、商户号和平台验签材料才能联调。
- ✅ Go 数据库适配层：PostgreSQL / MySQL 可由环境变量选择；文件模式仅限本地开发。数据库表自动初始化（见两份 SQL 脚本）。
- ⚠️ 当前运行环境无法下载 Go 数据库驱动或连接你的 PostgreSQL/MySQL，SQL 版尚未完成实际连接测试；需在可访问数据库的机器运行测试。
- ⚠️ 尚未在微信开发者工具打开、真机预览或提交微信审核；这些需真实 AppID 和部署信息。

## 本地启动（默认 PostgreSQL）

Windows PowerShell：在项目根目录执行 `Copy-Item backend/.env.example backend/.env`，编辑 `.env` 的数据库和密钥配置后执行 `./scripts/dev.ps1`。默认本地端口 **18080**；即使原来的 `.env` 仍是 8080，启动脚本也会覆盖本次运行的 `LISTEN_ADDR` 和 `PUBLIC_URL`（不修改 `.env` 文件）。如端口仍冲突，可以指定 `./scripts/dev.ps1 -Port 18081`，并同步修改 `miniprogram/config.js` 的 `API_BASE`。

```bash
cd backend
cp .env.example .env
# 修改 ADMIN_TOKEN、SESSION_SECRET（长度至少24）、数据库连接密码
# 首次拉取 Go 数据库驱动需要可访问 Go Module 代理
go mod tidy
set -a; source .env; set +a
go run .
```

健康检查：`curl http://localhost:18080/health`。数据库 `tongyin` 需预先创建并给账号授予建表权限；Go 启动时自动创建三张 `ty_*` 表及索引。

不想先启动数据库时，仅限本地调试：把 `DB_DRIVER=file`、`DEV_MODE=true` 写入 `.env`，会使用 `DATA_DIR/store.json`。

运营后台：`http://localhost:18080/admin/`，输入 `.env` 中的 `ADMIN_TOKEN`。

小程序：使用微信开发者工具导入 `miniprogram/`，将 `miniprogram/config.js` 的 `API_BASE` 改为本地地址或 HTTPS API 域名。在开发者工具的本地调试中可关闭域名校验（**仅用于本地调试**）；开发模式使用 `DEV_LOGIN: true` 并确保后端 `DEV_MODE=true`。正式环境必须双端禁用 DEV 模式。

## 待付款订单恢复与修改收货地址

- 作品详情页通过 `GET /api/works/{id}/order`（需要当前用户登录）检查当前作品是否已经存在未取消订单。若状态是 `awaiting_payment`，底部显示「继续付款」并跳转到原订单详情，绝不重复创建订单；其他已存在订单显示「查看订单」。
- 待付款订单的详情页支持「修改地址」，可选择微信收货地址或手动编辑姓名、联系电话及详细地址，再通过 `PUT /api/orders/{id}/shipping-address` 更新。接口必须验证订单所有权；仅 `awaiting_payment` 允许修改。修改只影响这笔订单，不覆盖用户常用地址或其他订单。
- 已付款、制作、发货和已完成的订单不允许通过该接口修改地址，避免下单后仓库打印发货信息产生不一致。若确有需要，后续应设计售后人工核验流程。
- 作品页自动识别已存在订单并展示该订单原有地址，避免展示用户不同的常用地址而误导付款。请求订单状态失败时不允许盲目再次下单；并为并发创建的 409 冲突提供恢复到原订单的路径。
- 后端测试覆盖订单所有权、取消订单不阻塞新下单、待付款修改地址后的服务端持久化，以及支付后无法修改；前端测试覆盖继续付款与编辑跳转。**支付成功仍必须配置正式微信商户并真实联调。**

## 微信用户登录与常用收货地址

- 小程序现有的 `ensureLogin()` 在正式模式下调用 `wx.login()`；Go 后端使用微信 `code2session` 换取用户 OpenID，并签发有效期 30 天的签名令牌。用户无需另外设置童印密码，**也不会自动获取微信昵称、手机号、收货地址**。获取微信收货地址只在用户主动点击时调用 `wx.chooseAddress()`。
- 本地 `DEV_LOGIN: true` 的用户固定为同一个 `dev-user`，仅用于开发者工具联调，不能用它测试多用户隔离；上线需设置 `DEV_LOGIN: false`、`DEV_MODE=false` 及有效的 `WX_APP_ID` / `WX_APP_SECRET`。
- 服务端 `GET /api/shipping-address` 返回当前已认证用户的常用地址；`PUT /api/shipping-address` 保存当前用户的单一默认地址。地址存储在后端 `ty_addresses` 表（PostgreSQL/MySQL），仅本地开发的 `DB_DRIVER=file` 存储到 `store.json`。所有读写都基于用户登录令牌对应的 OpenID，客户端不能指定其他用户。
- 首次进入下单页会读取服务端默认地址。**已有历史订单但未单独保存地址的用户**，会自动从其最近一单取出地址作为待用地址。用户可调用微信地址选择器，也可手动编辑并点击「保存为常用地址」。
- 成功创建订单时会把本次收货信息另存为该用户的最新常用地址；订单本身保留下单时的收货信息快照，后续修改常用地址不会修改历史订单。
- 微信 `wx.chooseAddress()` 需要用户自行确认/授权，接口在不同小程序主体与开发环境可能需申请权限；如果失败或用户拒绝，可使用手动编辑。准备发布前应在小程序隐私保护指引中说明姓名、联系电话、地址的收集用途、存储期限与删除方式。
- 本版仅提供一条「常用收货地址」，不做多地址簿及独立会员中心；上线前仍需完善用户信息删除/数据保留流程及生产环境备份和权限控制。

## 运营后台如何用来快速跑通一单

1. 小程序上传画作，点“开始 AI 变身”。
2. 后台 `/admin/` 的「第 2 步」为当前作品上传 **1 张** AI 形象图，立即变为 `image_ready`。
3. 小程序显示唯一一张形象图；满意则确认进入 `model_pending`，不满意可点击重新生成。
4. 后台第 3 步上传 **GLB** 文件后直接变成 `model_ready`。小程序在白模预览卡片内通过 Canvas WebGL 加载该 GLB，支持手指拖动旋转、双指缩放；无需上传静态三视图。旧版已上传 GLB 却卡在 `model_pending` 的作品，Go 服务重启时会自动迁移状态。
5. 小程序输入收件人信息，创建 `awaiting_payment` 订单。
6. 商户配置完成后调起微信支付；支付回调验签通过后变成 `paid`。
7. 后台将订单变成 `printing` → `shipped`（须填快递单号）→ `completed`。

后台**不允许**把未付款的订单改为制作中。

## 小程序内嵌 3D 预览（当前方案）

- **直接在同一个作品详情页**渲染 GLB 白模：预览区域由 `<canvas type="webgl">` 实现，可单指旋转、双指缩放；表单和下单按钮保持在同一页面。没有独立的 `web-view` 页面，也不再显示备用三视图。
- **最大化看细节**：预览卡片右上角「全屏查看 ⛶」会将当前 GLB 画布铺满小程序可用内容区域，提供「＋放大」「－缩小」「重置视角」和「退出全屏」；切换时保持已加载的模型和观察角度，不重新下载 GLB。微信原生导航栏仍由系统管理，不属于画布。
- 使用微信官方仓库 `wechat-miniprogram/threejs-miniprogram` 的 Three.js 小程序 WebGL 适配包和官方示例 GLTFLoader，源码已固定放在 `miniprogram/vendor/threejs-miniprogram/` 并包含 MIT 许可证，**不需要单独安装 npm**。该版本基于 Three.js r108，主要支持普通 GLB；带 Draco/Meshopt 压缩或特殊扩展的模型可能无法加载。
- 画布在作品进入 `model_ready` 后创建；按实际网格边界盒归一化尺寸和居中，使用白色哑光材质渲染；下载模型沿用 API 生成的签名 URL。图片截图仍可以作为历史数据保存，但**不参与预览页显示**，新上传模型也不必生成截图。
- 此实现避开此前导致微信开发者工具编译失败的 `xr-frame`，也避开无法作为普通卡片嵌入的 `web-view`。
- 开发者工具本地调试：后端默认 `http://localhost:18080`，需在微信开发者工具中关闭域名校验。手机真机必须使用可访问的 HTTPS 服务地址，不能使用手机自己的 `localhost`。
- 请验收：已有 GLB 的作品在同一预览页显示模型、单指旋转、双指缩放、点击「全屏查看」、全屏后放大缩小/重置视角/退出全屏、回到表单输入收件信息和下单，以及离开后重进。Three.js 小程序适配库较旧，**代码检查通过不代表在微信开发者工具或真机已验收**。异常时画布区域显示重试按钮和错误信息。
- 3D 预览只展示模型外观，不证明 3D 打印拓扑可制造性、尺寸精度或强度。

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

3D 阶段：`stage:"model"`、`model_url` 为 GLB 文件的 HTTPS 下载链接；`urls` 是可选的 0–4 张备用白模渲染图地址。回调请求头必须带 `X-AI-Token: <AI_WEBHOOK_TOKEN>`。后端会下载并保存生成文件，不依赖上游短期 URL。失败可回调 `{ "work_id": "...", "stage": "image", "error": "原因" }`。

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

现有 Nginx/HTTPS 反向代理指向 `127.0.0.1:18080`，见 `nginx.example.conf`。小程序后台需配置 `request` 和 `uploadFile` 合法服务器域名。浏览器通过 `https://api.example.com/admin/` 访问后台。

> 当前项目是**可本地运行的业务原型**，不是已上线的正式收款系统。上线前最少要完成：接已有数据库、对接 AI 提供商/人工生产流程、设置生产 HTTPS 与真实 AppID、实测微信支付回调和订单、配置访问控制/备份、完成小程序资质及隐私披露，并在开发者工具真机验收。

## 接下来需要你提供什么（不需要在聊天里发密码）

1. 数据库连接地址（本地 PostgreSQL，或服务器最终选择 MySQL/PostgreSQL）；密码只放在你本地 `.env`。
2. 对外 API 域名和服务器部署方式（Docker / 直接运行 Go），域名要有 HTTPS。
3. 小程序 AppID、微信支付商户权限是否已开通（敏感密钥留在服务器）。
4. AI 服务商或朋友使用的图生图/图生 3D 服务；暂时没有也能先走人工补齐结果。
