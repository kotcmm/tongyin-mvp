# 童印 MVP · AI 提供商与提示词配置

## 已实现

- 运营后台左侧 **AI 模型配置**（仅 owner 角色）：增删改多提供商，分别设置图片和 3D 默认模型、启用状态、模型 ID、API Key，修改图片/3D 提示词。
- 后端将 API Key 使用由 `SESSION_SECRET` 派生的 AES-256-GCM 密钥加密后存入现有数据库，后台只返回 `has_key`，**不回显原密钥**。请保管好稳定的 `SESSION_SECRET`，不要提交生产密钥。
- 图片生成：`seedream` 适配器调用火山方舟 `POST https://ark.cn-beijing.volces.com/api/v3/images/generations`，使用儿童画签名 URL + 提示词生成单张图片，下载后落本地 media 目录，再将作品改为 `image_ready`。
- 3D：`worker` 通用异步适配器。后端向配置的 Worker POST `work_id`、`stage`、`generation_id`、`source_url`、`callback_url`、`instructions`、`prompt_version`、`model`，其最终结果仍需调用已有 `/api/internal/ai-result` 回调。
- 不配置默认提供商时优先走旧的 `AI_WORKER_URL`，都不配置则保持后台人工上传。
- 原有用户上传原画 → 单张图片 → 用户确认 → GLB → 下单流程不变；旧作品保留。作品记录最后使用的提供商、提示词版本和发起时间。

## 启用 Seedream

1. 在方舟平台获取图片模型 ID 和 API Key（请先确认账号额度、计费策略）。
2. **保证 PUBLIC_URL 是公网可访问的 HTTPS 域名**，并且对方服务可以访问签名的 /media/ 图片地址。本地 `http://localhost` 对云端不可见，不适合做真实生图测试。
3. 后台用 owner 账号登录 → AI 模型配置 → 添加：能力「图片」，适配器 Seedream，填入模型 ID 和 Key，勾选「启用」和「默认」，保存。
4. 修改提示词（可选），在小程序上传原画并触发「生成」，查看作品生产中的状态。图片接口调用在 Go 服务内后台执行，返回结果后自动写入数据。
5. API 超时、Key 无效、额度不足、图片不可访问等错误会使当前作品标记 `failed`，后台可人工上传补救。生成调用会产生第三方账单；目前 **不包含费用上限、计费对账或分布式持久任务队列**。

注意：不同 Seedream 模型对于尺寸、图生图参数和输出格式支持范围不同，当前采用单图模式、`size=2K`。请在实际启用前对所选模型做一次小额端到端测试。

## 启用 3D Worker

1. 自行部署能将图片转成 GLB 的服务，且该服务实现约定的异步回调（**只填写一个模型 ID 不能生成 3D**）。
2. 后台新增能力「3D」、适配器「自定义异步 Worker」，填写 Worker HTTPS URL、Token（若需要），启用并设为默认。
3. Worker 接到任务后，处理结束发送：
```http
POST /api/internal/ai-result
X-AI-Token: <AI_WEBHOOK_TOKEN>
Content-Type: application/json

{"work_id":"...","stage":"model","generation_id":"...","model_url":"https://.../model.glb","urls":[]}
```
失败则回传 `{"work_id":"...","stage":"model","generation_id":"...","error":"失败原因"}`。回调 token 仅在服务端设置于 `AI_WEBHOOK_TOKEN`，不能暴露在小程序端。

## 安全与部署

- 仅 owner 可操作 AI 配置，任何小程序 API 都不会返回密钥。所有更新写入后台审计日志。
- 更换 `SESSION_SECRET` 会导致此前加密的 API Key 不可解密；建议在更换前先重新规划密钥迁移。
- 生成调用在进程内部 goroutine 中运行，**重启时未完成任务不会自动恢复**，需要运维排查并手工处理；后续接任务队列可增强可靠性。
- 回调结果下载要求 HTTPS、有效文件类型及体积限制；接入任意第三方结果地址时需特别注意内网请求风险。
- `DB_DRIVER=postgres` 与 `mysql` 会自动创建 `ty_ai_providers` 和 `ty_ai_prompts` 两张表，既有旧表不清理。
