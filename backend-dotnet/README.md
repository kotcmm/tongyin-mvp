# 童印 .NET 后端最小整洁架构

项目采用 .NET 10，按 **Domain / Application / Infrastructure / Api** 四层组织。

- `Tongyin.Domain`：领域实体及业务规则；目前仅实现了 **创建儿童画作品的初始状态**。
- `Tongyin.Application`：应用用例及外部端口；尚未迁移业务。
- `Tongyin.Infrastructure`：数据库及 AI 等外部接入；尚未迁移业务。
- `Tongyin.Api`：HTTP 程序宿主；尚未注册业务路由。

## 已实现的最小领域行为

`Tongyin.Domain.Works.Work.Create` 接收已上传原画的标识、所属用户、创建时间和存储路径，
创建处于 `image_pending` 状态的作品；默认没有候选图、预览图和已选图，
且 `ImageAttempts = 0`。

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
