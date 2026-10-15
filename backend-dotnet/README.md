# 童印 .NET 后端最小架构骨架

本目录只包含 .NET 10 Clean Architecture（整洁架构）的项目边界。当前没有业务模型、API 路由、数据库、AI、鉴权、支付等实现，不能替代 Go 后端。

## 结构

- Tongyin.Domain：领域层，当前为空，不依赖其他项目。
- Tongyin.Application：应用层，引用 Domain；未来承载业务用例和对外部服务的抽象接口。
- Tongyin.Infrastructure：基础设施层，引用 Application；未来承载数据库与外部平台接入实现。
- Tongyin.Api：Web 入口，引用 Application 与 Infrastructure；目前仅可启动空白 HTTP 宿主，不注册业务路由。

后续业务迁移遵循 AGENTS.md：一次只推进经用户确认的一个步骤，遵循先测后实现的顺序；业务逻辑不应进入 Api 项目。

## 最小结构检查

    python -m unittest discover -s tests -p "test_*.py" -v

## .NET 10 本地编译（需要安装 SDK）

    dotnet sln Tongyin.slnx list
    dotnet build Tongyin.slnx -c Release

待用户验收通过后，再选定一个业务用例，从调用方公开行为开始写测试；此阶段不迁移旧接口或修改 Go 系统。
