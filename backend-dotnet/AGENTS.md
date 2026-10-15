# AGENTS.md

## TDD 执行约束

采用 TDD（测试驱动开发）推进功能时，应遵守：

- 测试应从调用方真正需要的业务使用方式出发，允许直接描述当前尚不存在的类型、方法或属性。不要先看现有代码有什么，再围绕现有实现补测试。
- 测试应表达期望的公开行为，不要为了方便测试而引入只服务于测试的委托、Mapper（映射器）、Factory（工厂）或其他接缝。确实需要的架构边界应先说明其承载的真实业务或 CTP 协议职责。
- 一个测试用例只验证一个明确行为。Provider 选择、Client 行为、账户生命周期、连接结果、资源释放等不同职责应分别测试，不要混在同一个测试中。
- 严格按照“先写测试、确认红灯、再写最低限度实现、确认绿灯”的顺序执行。红灯可以是预期的编译失败。若用户只要求先写测试，报告红灯并停止；若用户明确授权同一轮完成 TDD，则记录红灯后仅实现当前测试要求的最小行为，再确认绿灯。
- 用户要求“先写测试”时，只能修改测试代码和必要的测试项目配置，不得同时修改产代码。
- 用户要求“实现”时，只实现当前已由测试明确要求的最小行为。没有测试覆盖的字段、状态、并发防御、锁、重复调用保护、生命周期方法、重连逻辑、便利方法、泛化抽象或未来扩展不得提前实现。
- 为通过当前测试如果必须修改更深层边界，应先说明测试暴露出的真实设计缺口。超出当前测试语义的修改，应先补对应测试或等待用户确认，不得自行扩大实现范围。
- 在分步骤任务中，“执行”只表示执行当前已经确认的一个步骤。如果用户明确授权本步骤包含完整的 TDD 红绿循环，可在该步骤内继续到最小实现和绿灯；完成当前业务用例后必须停止，不得自动推进下一用例。
- 不得为了让整条调用链立即编译通过而越过当前 TDD 层级。
- 每一步只运行与当前改动直接相关的最小测试集。预期红灯时应确认失败原因与当前缺失能力一致；绿灯时应确认没有通过额外的未测试实现绕过测试意图。

## 代码注释约定

新增或修改 `C#` 产代码时，应遵守：

- public、internal 类型必须有中文 XML 注释。
- public、internal 属性必须有中文 XML 注释。
- public、internal 方法必须有中文 XML 注释。
- public、internal enum 及 enum 成员必须有中文 XML 注释。
- 一个 public、internal class / record / interface / enum 应使用一个独立 `.cs` 文件，文件名应与 public、internal 类型名一致。
- 如果使用英文术语，应在附近给出中文解释。

## 代码命名约定

新增或修改 `C#` 产代码时，应遵守：

- 所有public相关的定义都是大驼峰
- 所有私有字段都是下划线+小驼峰，例如：_abcName

- 方法参数默认使用“展开”格式，即方法签名与参数列表优先写在同一行。
- 例如：`internal static SessionProtocolInput Create(SessionEvent sessionEvent, int currentRequestId, AuthenticateInfo? lastAuthenticateInfo)`
- 不要使用“收缩”格式，例如：
  ```csharp
  internal static SessionProtocolInput Create(
      SessionEvent sessionEvent,
      int currentRequestId,
      AuthenticateInfo? lastAuthenticateInfo)
  ```

## 代码结构

一个类或者一个接口都需要单独一个文件
