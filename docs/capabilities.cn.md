# 能力与证据

BGCS 生成源码；解析、生成、消费编译和真实调用是不同状态。4.0 的证据必须绑定当前源码和实际包，历史3.x结果不自动继承为当前认证。

## 基础组合

标准组合提供受支持桌面宿主资源、桌面目标头文件环境、CoreCLR profile 和已实现的调用 ABI 规则。GNU 与 musl 分开；未知 libc、缺少 parser 资源或目标 SDK 不完整时明确失败。

| 可选实现 | 声明范围 |
| --- | --- |
| BrowserWasm | Emscripten 目标与 Mono Wasm profile、静态表和 callback 策略。真实 Mono/AOT aggregate 回归属于对应仓库验收。 |
| NativeAot | NativeAOT profile 与已有调用机制，不执行 publish。 |
| Android | NDK/目标输入，不凭目标名推定 C# runtime。 |
| AppleMobile | 设备、模拟器与 SDK 输入，不能将两者混同。 |
| FreeBsd | 目标/sysroot 输入，不自动宣称 CoreCLR 或原生调用认证。 |

资源存在、能够选择目标和可以生成源代码，不能写成完整平台认证。ARM64 等未执行组合保持未验证。特殊 SDK 通过私有 provider 接入；新的 ABI 或执行机制需要相应实现。

## 声明级报告

`GenerationResult.capabilities` 与 `metadata/capability.report.json` 记录选中、排除、原生桥接、raw/friendly 生成或拒绝原因，包含 runtime/profile 摘要和调用 provider。源码要求另记录物理模块与导出映射。

证据级别为 Unknown、Inferred、Declared、UserAsserted、Verified。provider 声明不自动成为 Verified；生产生成不会执行原生调用探针。

未知所有权或编码时，不制造对应友好包装；可正确表达的 raw 入口保留，并说明用户需要承担的协议。未知按值 ABI 不能通过相同 sizeof 放行，应使用已有 pointer/shim 机制或明确拒绝。

执行结果和平台范围以[发布历史](releases/README.md)所链接的同提交 CI、包及公共消费回执为准。[历史台账](history/4.0-development-ledger.md)描述中间尝试，不表示当前平台状态。
