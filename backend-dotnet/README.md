# 童印 .NET 后端最小整洁架构

项目采用 .NET 10，按 **Domain / Application / Infrastructure / Api** 四层组织。

- `Tongyin.Domain`：领域实体及业务规则；目前实现了作品创建及图片、模型生成阶段的领域状态转换，未接入外部服务。
- `Tongyin.Application`：应用用例及外部端口；尚未迁移业务。
- `Tongyin.Infrastructure`：数据库及 AI 等外部接入；尚未迁移业务。
- `Tongyin.Api`：HTTP 程序宿主；尚未注册业务路由。

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

## 验收

需要 .NET 10 SDK：

```powershell
dotnet build backend-dotnet/Tongyin.slnx -c Release
dotnet test backend-dotnet/tests/Tongyin.Domain.Tests/Tongyin.Domain.Tests.csproj -c Release
```

仅检查骨架结构：

```powershell
cd backend-dotnet
python -m unittest discover -s tests -p "test_*.py" -v
```

新增功能遵守同目录 `AGENTS.md`：以调用方的业务需求写测试，记录红灯，再实现最小代码并验证绿灯。
一次只完成一个确认过的业务用例，后续迁移需另行确认。

**此分支不替代已运行的 Go 服务。** 不允许把尚未迁移的代码当成完整后端，也不应直接合并上线。
