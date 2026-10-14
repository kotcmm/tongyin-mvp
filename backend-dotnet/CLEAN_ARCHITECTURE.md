# 童印 Clean Architecture V2 迁移说明

## 核心设计

**依赖只能向内，不能向外。** Domain 和 Application 两层不得出现 `HttpContext`、
`IFormFile`、`IResult`、`JsonObject`、`IStore` 或任一数据库驱动的引用。

```text
                     Tongyin.Api
                    /     |      \
                   v      v       v
          Application   Infrastructure
                   |      /      |
                   v     v       |
                    Domain <-----+
```

- `Domain`：强类型聚合 `Work`、`Order`，值对象 `ShippingAddress`，与业务规则一起封装的状态转换。
- `Application`：显式 Use Case、`IWorkRepository`、`IOrderRepository`、`IWorkGenerationGateway`。
- `Infrastructure`：`LegacyWorkRepository`、`LegacyOrderRepository` 实现持久化端口；`LegacyWorkDocument` 和 `LegacyOrderDocument` 负责 Go JSON 的双向映射，写入时保留未知字段。
- `Api`：已有 Controller 与 `Presentation/` HTTP 适配器。现有 `AiService` 的过渡生成网关目前在 Api/Adapters；后续应将 AI 客户端移至 Infrastructure。

项目间引用：Application → Domain；Infrastructure → Application + Domain；Api → 所有模块。

## 已落地的业务切片

| 业务行为 | Domain | Application | Infrastructure | API |
|---|---|---|---|---|
| 创建作品 / 上传原画 | Work.Create | WorkUseCases.CreateAsync | LegacyWorkRepository | WorkService.UploadDrawing |
| 我的作品 / 详情 | Work 状态 | ListAsync / GetAsync | LegacyWorkRepository | WorkService.ListWorks / GetWork |
| AI 图片生成排队 / 次数限制 | Work.RequestImage | GenerateAsync + 生成网关 | LegacyWorkRepository | WorkService.GenerateImage |
| 选择形象 / 进入 3D 阶段 | Work.SelectFirstImage | SelectAsync + 生成网关 | LegacyWorkRepository | WorkService.SelectImage |
| 创建订单（事务及重复订单保护） | Order.Place / EnsurePlaceable | OrderUseCases.PlaceAsync | LegacyOrderRepository | OrderService.CreateOrder |
| 取消待支付订单 / 写审计 | Order.Cancel | OrderUseCases.CancelAsync | LegacyOrderRepository | OrderService.CancelOrder |

## 迁移约束

1. **先验收，再切换**。原 Go 服务与 `feat/dotnet-backend-migration` 不做任何清理或合并。
2. `IStore` 仍是基础设施内部现有存储机制，Application 只能通过强类型 Repository 接口访问；不把全库快照泄漏到内层。
3. 原 `ty_*` payload 和未知字段必须保留，避免旧作品/订单数据丢失。
4. 用户端接口方法、URL、返回体、错误码必须与原 Go 兼容；每个用例都需要针对旧行为增加集成测试。
5. `18080` 保持不变；Go 和 .NET 不能同时监听该端口。

## 尚未完成（不可直接上线）

- 订单查询/地址读写/物流轨迹仍使用旧业务层。
- 后台管理、权限控制、AI 配置/回调、微信支付、物流调用仍需迁入用例与网关。
- ASP.NET Core `HttpContext` 和 `IResult` 仍存在于未迁移的 `Tongyin.Api/Presentation` 目录中：这是迁移期间的旧 HTTP 适配器，**不是 `Tongyin.Application` 项目**。
- 真实 Seedream、3D、微信支付和物流联调尚未验收；旧库副本对比仍待执行。

## 本地验收

在仓库根目录：

```powershell
git fetch origin
git switch --track origin/feat/dotnet-clean-architecture-v2
dotnet build backend-dotnet/Tongyin.slnx -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File backend-dotnet/verify-local.ps1
```

其中新增 `Tongyin.CleanArchitectureChecks`：脱离 ASP.NET 的用例测试，
使用真实 legacy Repository 和内存 IStore 测试：用户隔离、生成次数、选图、订单重复创建、
取消限制、审计、未知字段保留。

如果有任何编译或行为回归错误，先修复和验证，不得合并 PR。
