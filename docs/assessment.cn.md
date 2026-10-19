# BGCS 3.0 七项工程评估

[English](assessment.md) | [能力与边界](capabilities.cn.md) | [验收](acceptance.cn.md)

评估日期：2026-10-19。本文评估当前仅负责生成的产品实现及源码边界，
不宣告全部功能验收完成，不认证某个发布 revision，也不表示软件包已经发布。

核心功能基线属于强制验收。源码/API/架构检查、本地可执行 fixture、同 revision 验收回执
分别证明不同的事实，应分开记录。AOT/Wasm 应用编译和运行属于可选的私有消费者检查，
不是产品强制验收；这些 profile 的目标事实和源码生成仍属于生成器范围。

## 七项判断

| 维度 | 当前评估 | 仍需正视的限制 |
| --- | --- | --- |
| 功能 | C/C++ 源码生成、三种 import、package/source Runtime、实例/静态呈现、插件、缓存和工作区 有实现路径及针对性 fixture。 | Raw ABI、native holder 和友好托管投影范围不同。源码存在不能直接关闭每项基线或构成发布认证。 |
| 代码优雅 | 不可变模型、有限类型化操作、明确拒绝和最终生成契约检查有助于审阅。 | ABI 与生命周期要求较多 planner/verifier/cleanup 代码。格式和测试通过不证明已经达到理想简洁，也不消除维护风险。 |
| 架构整洁 | 十一个产品工程有明确依赖边界；中立分析和托管发射不持有 Clang AST。 | Host 编排仍有相当复杂度；新增语义应归属相应层，公共 helper 的增长需要审阅。 |
| 可扩展性 | Contracts-only 工厂覆盖输入、目标描述、变换、lowering、projection、发射、产物处理和验证；状态与外部输入影响缓存资格。 | 插件必须提供完整身份、证据和生命周期契约。高级 Clang 贡献器刻意采用不同的依赖与兼容边界。 |
| 使用简易性 | 分组配置及 init/doctor/validate/generate/diff 提供统一生成流程；默认实例 API、FunctionTable、分文件，消费者自行接入生成。 | 需要配置头文件和目标 SDK 输入；编码、allocator、具体模板和生命周期可能需要明确声明。消费者负责编译、链接与部署。 |
| 解耦性 | 原生事实、bridge 操作、interop ABI 和托管呈现分开建模；worker 隔离解析，Runtime 不依赖生成器。 | 替换 frontend 必须提供等价事实；增加目标描述本身不能证明 ABI 支持。 |
| 跨平台性 | 宿主解析、目标身份/SDK/ABI、runtime/import 能力分离；GNU 与 musl 保持不同目标事实。 | 目标建模和代码生成不等于应用已经在该平台验证。未知 profile 保持明确拒绝；不可阻塞 browser profile 当前仍省略拥有型虚代理注册。 |

## 实现颗粒度

十一个工程划分的是会独立变化的职责。类型化模型以及按协议拆分的 planner、emitter、
verifier，让普通扩展主要落在所属模块；按每个类型或平台继续增加工程并不会改善边界。
但这不代表每个实现都已经足够小。

`BGCS/Pipeline/GenerationPipeline.cs` 仍是集中点：约 900 行的生成方法包含局部辅助函数，
协调目标解析、lowering、投影、缓存、后处理与最终发布。模块调用方向正确，但方法规模
增加了审阅控制流和失败清理的成本。配置加载器与最终源码契约读取器也较大。
架构和格式检查通过不能消除这些维护债务，本文不将其实现颗粒度评价为最优。

## 产品职责

| 工程 | 职责 |
| --- | --- |
| BGCS.Contracts | 无其他 BGCS 依赖的中立模型和普通扩展契约。 |
| BGCS.Infrastructure | 输入快照、缓存/发布、插件加载与 worker 传输。 |
| BGCS.Frontend.Clang | 按配置的目标输入解析 C/C++，分离出编译器事实。 |
| BGCS.Frontend.Clang.Worker | 在独立进程运行解析器和高级贡献器。 |
| BGCS.Platforms | 解析目标、ABI 和只读工具链/SDK 描述。 |
| BGCS.Analysis | 选择/闭包、布局和语义/安全验证。 |
| BGCS.Cpp2C | 规划 C++ lowering，生成 C ABI bridge 源码与原生断言。 |
| BGCS.CSharp | 规划托管呈现、生成 C# 并检查最终生成契约。 |
| BGCS.Runtime | 绑定调用、内存和生命周期基础设施，不依赖生成器。 |
| BGCS | 组合生成、扩展会话、输入复核和产物发布。 |
| BGCS.Tool | 通过 CLI 暴露生成器。 |

BGCS 生成源码，由外部构建系统编译和链接。产品不执行原生构建，不用选定编译器的预处理
声明替换解析结果，也不把检查原生目标文件作为生成步骤。发射的原生断言是交给消费者
编译器的源码，不表示这些断言已经编译通过。托管投影输入是供静态 Roslyn 绑定使用的
源码、元数据和 defines；不会启动消费者工程、发射/加载其程序集或运行布局探针。
标量字段复制投影保留生成的 Raw ABI carrier，未知托管布局不能成为直接 ABI 复用的依据。

Contracts 和 Runtime 无其他 BGCS 工程依赖，普通 lowering 使用中立契约。
冻结注册、会话释放、输入快照和独立产物检查共同维护边界。登记的 Frontend.Tests 友元
属于测试例外，生产友元及共享生产编译源码仍被禁止。

## 如何扩展

普通扩展引用 `BGCS.Contracts`，通过 `IBindingPlugin` 注册会话拥有的工厂。
`ICppTypeLowering`、`ICppCallableLowering` 处理类型化 C++ 协议，`IManagedApiTransform`
处理托管呈现，`ISourceContributor` 提供源码输入，`INativeTargetProvider` 提供目标事实，
`IBindingBackend` 负责源码发射。有限的库专属转换也可使用 recipe 或显式 native shim。

插件 manifest 声明兼容范围、服务身份/优先级、设置和锁定输入。
`ICacheFingerprintProvider` 描述状态及外部依赖；指纹不完整时，相应缓存不能复用。
CLI、SDK、workspace 接入同一生成流程。完整步骤见
[扩展](extensibility.cn.md)和 [C++ 手册](cpp-extension-cookbook.cn.md)。

需要解析器专属事实时使用高级 `IClangFactContributor`。它在 worker 内依赖精确解析器 API，
借用句柄不能跨出贡献调用；普通 Contracts-only 插件不必引入此依赖。

## 大多数库是否能轻松绑定

公开接口为固定参数 C ABI、布局可表达的库通常最直接。string/Span/owned 等友好 API
仍需补充头文件没有表达的语义。C++ 对象、继承、具体模板实例及有限标准/自定义家族协议
已有路径，但任意模板或容器结果不会自动变成自然 C# API。可变参数需要明确的有限 wrapper；
chrono 值保留真实原生单位和时钟事实，不虚构 epoch。未知 ownership、清理或 ABI 分类
会导致拒绝、明确的友好层省略，或要求显式 shim。

没有足够证据承诺“大多数任意 C++ 库都很容易”。复杂 allocator、callback、取消、
自定义容器和编译器专属头文件分支需要项目工作。Clang 解析事实不能证明任意下游编译器
配置都等价。仓库 fixture 可以编译、链接和调用生成绑定，作为核心功能证据；这些工作
位于产品外部。有限检查也不认证整个 GUI、音频设备或 GPU 系统。

剩余完成判断应针对当前核心基线的实际证据，并另行处理适用的分发/发布流程。
可选的私有 AOT/Wasm 应用检查应标明真实宿主、revision 和范围；无论有没有执行这些检查，
都不能把源码生成能力升级为普遍的平台认证。
