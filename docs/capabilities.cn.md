# BGCS 3.0 能力与边界

[English](capabilities.md) | [全方位评估](assessment.cn.md) | [验收](acceptance.cn.md)

本文描述当前 3.0 实现。源码存在、本地测试通过、发布 revision 获得认证是三种不同
结论。CI 配置存在和旧版本报告都不能自动认证当前源码。

## 如何阅读证据

| 状态 | 能证明什么 |
| --- | --- |
| 已实现源码 | 存在模型、实现和验证路径。 |
| 本地证据 | 指定测试或 fixture 在记录的宿主及编译字节上执行通过；未提交工作区仍是本地证据。 |
| revision 认证 | 同一发布 revision 的所需作业、真实调用和干净安装消费全部通过，且平台范围明确。 |
| 明确限制 | 缺少 ABI 或生命周期证据时拒绝、保留 Raw，或报告未生成友好 API 的原因。 |

当前本地工作包含 Windows/MSVC 原生调用、CoreCLR 导入及 Runtime 模式矩阵、NativeAOT、
Chromium 浏览器解释/AOT、外部插件和真实库 fixture。它们来自不同检查点、证明范围不同，
不能拼成未经验证的最终 revision 认证。发布状态由[门禁清单](../eng/acceptance/required-release-gates.json)、
功能/配置 inventory 及同 revision 报告决定。

## C 声明与 ABI

| 能力 | 当前行为与边界 |
| --- | --- |
| 函数、枚举、别名、标量/字符串常量和宏 | 中立编译器事实保留身份、符号位、精确值、码元和来源。依赖探测位置的宏不会伪装为稳定常量；任意函数宏需要显式类型化原生适配器。 |
| 记录、union、数组、packing、位字段 | 原生布局、托管存储、按值调用分别验证；相同大小从来不等于调用安全。 |
| 指针别名与 opaque 记录 | 可表达的 Raw 指针仍可用。显式地址 carrier 不拥有对象；unsafe 解引用视图仍由调用者负责有效期。 |
| 字符串、buffer 与单值指针 | string/Span/ref/out 需要编码、单位、边界、方向和生命周期事实；未知指针不会自动成为字符串或 Span。 |
| 可变参数 | `source.variadicVariants` 生成有限固定原生包装，由原生编译器完成默认参数提升，固定 ABI 可使用三种 import。不会生成任意托管 varargs 调用；仅保存不调用的可变参数函数地址也不代表通用调用签名。 |
| C internal/inline helper | `source.includeHeaderHelpers` 默认 false；启用后适配器必须编译到同一配置模块，现有 DLL 不会自动增加导出。编译器 intrinsic 需要有限的 provider 专属证明。 |
| 特殊基础类型 | `char`、`long`、`wchar_t`、`char8_t`、`long double`、`va_list` 保留目标事实；不支持的按值形式被拒绝，指针和 native shim 路径需要显式选择。 |
| 导入方式 | FunctionTable、DllImport、LibraryImport 共享 interop 模型，但仍受运行时/profile 能力约束；换 import 不能让不支持的 ABI 变安全。 |

## C++ 桥接与托管投影

| 能力 | 当前行为与边界 |
| --- | --- |
| 对象、成员、运算符、重载 | 类型化操作覆盖可访问的具体声明、构造/析构、status/error 边界及原生 cast；deleted、不可访问或未解析声明明确失败。 |
| 模板与继承 | `cpp.templateInstantiations` 显式选择有限的类/函数实例；多继承使用真实原生指针调整，不替换成 reinterpret cast；任意模板元编程不会自动实例化。 |
| 标准与配置类型家族 | string、vector/span/array、map/set、optional/variant/expected、unique/shared pointer、path、duration/time point 使用有限类型化协议。`cpp.familyBindings` 基于实际编译器类型/模板事实及显式行为断言，名字或大小相似不能证明兼容。 |
| 字符串与路径 | counted 适配器保留嵌入零及声明的 UTF 码元。新建默认标准 string/path 结果可在证明成立的同步 creating-call 清理作用域内复制成 C# string；自定义 allocator/traits/生命周期需要自己的证明或 shim。 |
| 其他容器结果 | Raw/status holder 操作与高级托管集合返回是独立能力；原生容器可用不等于自动返回 C# 数组或集合。 |
| Chrono | 保留实际 count、representation、ratio、clock；不推断统一纳秒换算或托管日期 epoch。 |
| Ownership 与异常 | 拥有型包装需要精确析构配对与清理线程域；C++ 异常在生成的 C 边界内处理；未知 retention 或释放线程要求不会被编造。 |
| 虚回调代理 | `cpp.virtualCallbacks` 提供精确 Raw proxy 协议；满足条件时生成托管 thunk，并要求显式完整原生 stop/join。未知清理保留 Raw；Browser Wasm 等不可阻塞 profile 目前不生成拥有型托管虚代理注册，普通 retained C callback/async 不能证明这个不同协议。 |
| 自定义 lowering | Contracts-only 类型/调用 lowering、有限 recipe、受控片段和 native shim 可扩展流水线；用户断言与外部输入保留其证据等级和缓存身份。 |

具体配置见 [lowering](lowering.cn.md) 与[实战手册](cpp-extension-cookbook.cn.md)。
返回 `char*` 并不证明 UTF-8 或 ownership，函数名为 `Create` 也不证明存在安全释放函数。

当前产品接受 C/C++，Objective-C 模型与分析不在仅生成范围内；无法支持的 block/调用形状仍明确拒绝。

## Runtime、扩展与工作流

实例上下文、FunctionTable 加载、module/call/resource lease、owned 和 module-borrowed
handle、call-scoped callback、显式 retained callback 协议及有限异步 completion 投影均有实现。
回调异常被拦截；浏览器的非阻塞 retention/completion 需要显式 event-loop 协议，不能套用
桌面阻塞 stop/join。

普通插件只引用 `BGCS.Contracts`。源码贡献、目标、native/interop/managed transform、
C++ lowering、managed projection/backend、产物处理和验证均有
类型化端口。高级 Clang fact 插件刻意使用独立的 parser-pinned worker-only 接口与借用句柄，
见[扩展文档](extensibility.cn.md)。

CLI、SDK、workspace 使用同一 BindingHost。冻结的分组配置、确定顺序、
依赖快照与扩展指纹控制缓存；命中缓存仍执行本次所需验证。最终契约检查和所属产物发布保护
此前完整输出。

默认值是 .NET 10/C# 14、C17/C++23、实例 API、FunctionTable、分文件、package Runtime、
不构建原生；单文件默认 `Bindings.cs`。完整 SDK/tool 交付内置功能，用户不必逐个安装实现工程。
准确字段及默认值来自[生成的配置参考](generated/config-reference.cn.md)。

## 平台范围

必需 CI 是 Windows 完整生成器检查、Linux/macOS 生成器 smoke 和一个完整产品安装任务。
核心基线和原有各宿主性能预算不变。AOT/Wasm 应用构建/运行仅为可选手动私有检查，
相应目标事实与源码生成仍在范围内。ARM64、musl、Android、iOS、FreeBSD 的描述不自动
认证目标运行，Chromium 通过也不认证其他浏览器。应用/平台调用结论需自己的准确回执，
与源码生成验收分开记录。
