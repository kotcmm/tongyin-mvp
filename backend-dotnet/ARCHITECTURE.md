# 童印 Clean Architecture 设计与迁移验收

## 目标与边界

- 原 Go 后端不得删除或覆盖；本分支从已通过本地验收的 `feat/dotnet-backend-migration` 分出。
- 完全保留现有小程序 API Method、URL、JSON 字段、HTTP 状态码、Go 数据文件和 SQL `ty_*` payload 的兼容性。
- 应用层不依赖 Web、数据库、OpenAI/Seedream SDK 或其它外部系统。
- Domain 不依赖任何其它本项目程序集；Application 只依赖 Domain，Infrastructure 依赖 Application/Domain，Api 负责组合所有组件。

```text
Tongyin.Api                      HTTP / Controllers / Composition Root
   |----------------------+----------------------------+
   v                      v                            v
Tongyin.Application     Tongyin.Infrastructure       Tongyin.Domain
   |                      |                           ^
   v                      +---------------------------+
Tongyin.Domain           Application
```

## 项目说明

| 项目 | 职责 | 不应该做的事 |
|---|---|---|
| Domain | 领域不变量、作品/订单规则、现有 Go JSON 快照兼容模型 | 依赖 ASP.NET、HTTP 请求或数据库 |
| Application | Use Case、业务事务编排、持久化/外部服务的端口接口 | 使用 HttpContext、IResult、SqlConnection、IFormFile |
| Infrastructure | IStore 的 File/PostgreSQL/MySQL 实现、AI/签名/媒体等适配器 | 决定订单业务状态、返回特定 HTTP 状态 |
| Api | Controller、模型绑定、HTTP 错误格式、静态后台、依赖注入 | 越过 Application 直接编排复杂业务事务 |

## 已迁移的垂直切片

- `WorkUseCases.Create`：创建作品记录
- `WorkUseCases.QueueImage`：形象生成次数与任务状态转换
- `WorkUseCases.SelectImage`：选图并进入模型生成阶段
- `WorkUseCases.List / Find`：按用户隔离读取作品
- `OrderUseCases.Create`：检查作品状态并原子写入订单及收货地址
- `OrderUseCases.Cancel`：检查取消条件、更新历史和审计
- `OrderUseCases.List / Find / GetWorkOrder`：按用户隔离读取订单
- `OrderUseCases.GetSavedAddress / SaveAddress / UpdateShippingAddress`：订单地址业务
- `WorkRules`、`OrderRules`：纯领域规则，异常和提示内容保持旧版兼容

`Tongyin.ApplicationChecks` 只引用 Domain/Application，不引用 Web/Infrastructure，因此可以脱离 ASP.NET 和数据库测试业务不变量。

## 已知技术债 / 后续顺序

1. 继续把管理员权限/审核、AI 配置和回调、退款、发货/物流的业务规则下沉到 Application。
2. 由 Application 定义 `IAuthContext`、`IMediaStorage`、`IAiGenerationGateway`、`IPaymentGateway`、`ILogisticsGateway` 端口；Infrastructure 实现；Web 请求转换在 API 完成。
3. 将 `Api/Presentation` 中目前兼容旧代码的 `HttpContext` 和 `IResult` 限制到 Controller，彻底移除过渡处理类。
4. 将应用层 JSON 输入/输出逐步替换成强类型命令/DTO；领域状态替换成显式状态模型。迁移时必须保持原 `ty_*` JSON 序列化格式。
5. 针对 Go/C# 同一套输入和数据库副本执行行为回归；再进行真实 3D、AI、微信支付、物流联调。

目前仅第一个步骤的部分垂直切片完成；**四项目依赖图正确并不代表业务迁移已经结束**。

## Windows 验收

从仓库根目录：

```powershell
git switch feat/dotnet-clean-architecture
git pull --ff-only origin feat/dotnet-clean-architecture
dotnet build backend-dotnet/Tongyin.slnx -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File backend-dotnet/verify-local.ps1
```

通过自动检查后才运行 `backend-dotnet/run-local.cmd`；服务监听原本的 `18080`，不要和 Go 服务同时启动。小程序配置不变。

