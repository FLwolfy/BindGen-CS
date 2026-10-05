# BindGen-CS 中文 Wiki

[English](README.md) | [简体中文](README.cn.md) | [仓库 README](../README.cn.md)

完整重构的本机结果见[验收记录](architecture-refactor-acceptance.cn.md)，
[逐项核对](architecture-plan-audit.cn.md)将批准的 Plan、实际边界和独立证据对应起来。
其他宿主实机和远程发布状态单独记录。

## 第一次使用

1. [快速开始](getting-started.cn.md)：从 header 到可编译的 bindings。
2. [能力与边界](capabilities.cn.md)：先确认目标 ABI/C++ 特性是否在支持范围内。
3. [当前契约与发布证据](compatibility-policy.cn.md)：配置、公开 API、目标支持与供应链证据。
4. [配置指南](configuration-guide.cn.md)：选择 preset、target、import mode 和 marshalling policy。
5. [诊断指南](diagnostics.cn.md)：处理 parser、ownership、buffer、callback 和 C++ lowering 问题。

## 按任务查找

| 任务 | 文档 |
| --- | --- |
| C Binding | [快速开始](getting-started.cn.md) |
| C++ → C Bridge | [快速开始的 C++ 章节](getting-started.cn.md#c-bridge)、[具备专门回归测试的 C++ 配置条目](cpp2c.config.md) |
| 扩展复杂 C++ 语义 | [最终 lowering 架构](lowering.cn.md)：声明式 recipe、typed lowering plugin、显式 C shim 与安全策略 |
| 从零实现项目适配 | [C++ 扩展实战手册](cpp-extension-cookbook.cn.md)：可运行 shim/plugin、callable recipe、决策树与 lifetime 模式 |
| 完整配置属性 | 运行 `bindgen-cs schema bindgen.schema.json` |
| 长期执行顺序与 gate | [超级通用执行路线图](roadmap.cn.md) |
| 判断当前是否已达到“超级通用” | [工程成熟度审计](assessment.cn.md) |
| 具备专门回归测试的配置条目 | [生成配置条目参考](config.md) |
| 嵌入 C# 工具 | [C# API 参考](api.md) |
| 验证 Wasm 中的实际原生调用 | [独立测试流程](testing.md#independent-webassembly-invocation)、[本次验收](wasm-acceptance-2026-10-03.cn.md) |
| 选择 NuGet 包 | [NuGet 包与公开 API](packages.cn.md) |
| 运行测试与验收 | [测试说明](testing.md)、[验收规范](acceptance.cn.md) |
| Clang 资源与跨宿主 CI | [CI 宿主与目标边界](ci-portability.cn.md) |
| 发布包 | [发布说明](publish.cn.md) |

## 设计与质量

- [通用 C# 开发规范](csharp-development-standard.cn.md)：独立于消费方的命名、排版、API 与生命周期要求。
- [平台架构完整重构计划](platform-architecture-refactor-plan.cn.md)：目标目录、依赖边界、执行顺序与验收矩阵。
- [架构说明](architecture.cn.md)：分层、依赖规则和 IR-native 数据流。
- [平台扩展与 Solution 组织](platform-extension.cn.md)：宿主/目标区分、iOS 目标描述、新 provider 接入和工程分组。
- [C++ 扩展实战手册](cpp-extension-cookbook.cn.md)：从配置到真实 native/C# invocation 的高级扩展教程。
- [验收规范](acceptance.cn.md)：9.0 gate、性能预算和 target 隔离。
- [能力与边界](capabilities.cn.md)：实现、证据和未覆盖范围的对照表。
- [超级通用执行路线图](roadmap.cn.md)：分阶段任务、不可妥协规则和 9.0 完成条件。
- [工程成熟度审计](assessment.cn.md)：量化评分、五项工作状态和仍不可宣称完成的边界。

## 当前成熟度

CLI/workspace、安全输出事务、SingleFile、target/toolchain model、共享 IR、安全分析、NuGet 闭包、真实库快照、声明范围内的 STL/smart-pointer lowering、lifetime diagnostics 与发布治理 gate 均已可用。desktop-x64 BGCS 报告仍是独立发布证据。

macOS arm64 已有本地完整验收。9 月 24 日用户提供的 desktop-x64 CI 日志显示三个 runner 全部失败；本 checkout 中的修复仍须在同一 revision 的 CI 中重跑，才能把这些平台标为通过。报告中的 9.0 是 gate 通过后的固定标签，不是独立校准的质量分数。C# 输出只使用 IR-native emitter，不加载任何旧的预发布配置或 emitter。见 [CI 故障分析](ci-remediation-2026-09-24.md)。
