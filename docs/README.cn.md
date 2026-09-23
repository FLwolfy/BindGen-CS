# BindGen-CS 中文 Wiki

[English](README.md) | [简体中文](README.cn.md) | [仓库 README](../README.cn.md)

## 第一次使用

1. [快速开始](getting-started.cn.md)：从 header 到可编译的 bindings。
2. [能力与边界](capabilities.cn.md)：先确认目标 ABI/C++ 特性是否在支持范围内。
3. [兼容与废弃政策](compatibility-policy.cn.md)：配置版本、公开 API、废弃周期与供应链证据。
4. [配置指南](configuration-guide.cn.md)：选择 preset、target、import mode 和 marshalling policy。
5. [诊断指南](diagnostics.cn.md)：处理 parser、ownership、buffer、callback 和 C++ lowering 问题。

## 按任务查找

| 任务 | 文档 |
| --- | --- |
| C Binding | [快速开始](getting-started.cn.md) |
| C++ → C Bridge | [快速开始的 C++ 章节](getting-started.cn.md#c-bridge)、[具备专门回归测试的 C++ 配置条目](cpp2c.config.md) |
| 扩展复杂 C++ 语义 | [最终 lowering 架构](lowering.cn.md)：声明式 recipe、typed lowering plugin、显式 C shim 与安全策略 |
| 完整配置属性 | 运行 `bindgen-cs schema bindgen.schema.json` |
| 长期执行顺序与 gate | [超级通用执行路线图](roadmap.cn.md) |
| 判断当前是否已达到“超级通用” | [工程成熟度审计](assessment.cn.md) |
| 具备专门回归测试的配置条目 | [生成配置条目参考](config.md) |
| 嵌入 C# 工具 | [C# API 参考](api.md) |
| 选择 NuGet 包 | [NuGet 包与公开 API](packages.cn.md) |
| 运行测试与验收 | [测试说明](testing.md)、[验收规范](acceptance.cn.md) |
| 发布包 | [发布说明](publish.md) |

## 设计与质量

- [架构说明](architecture.cn.md)：分层、依赖规则和 IR-native 数据流。
- [验收规范](acceptance.cn.md)：9.0 gate、性能预算和 target 隔离。
- [能力与边界](capabilities.cn.md)：实现、证据和未覆盖范围的对照表。
- [超级通用执行路线图](roadmap.cn.md)：分阶段任务、不可妥协规则和 9.0 完成条件。
- [工程成熟度审计](assessment.cn.md)：量化评分、五项工作状态和仍不可宣称完成的边界。

## 当前成熟度

CLI/workspace、安全输出事务、SingleFile、target/toolchain model、共享 IR、安全分析、NuGet 闭包、真实库快照、声明范围内的 STL/smart-pointer lowering、lifetime diagnostics 与发布治理 gate 均已可用。InnoEngine 五库生成 workspace 已在 macOS Arm64 完成 clean regeneration、全部 native dependency 构建、完整 solution 构建、手写 import 审计与全部 native-binding tests；desktop-x64 BGCS 报告仍是独立发布证据。

当前源码已经产出完整通过的 `macos-arm64-darwin` 报告，十个分类全部达到 9.0/10；Windows x64、Linux x64、macOS x64 仍需同版本完整报告。C# 输出只使用 IR-native emitter，不存在预发布 compatibility fallback。
