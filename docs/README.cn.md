# BGCS 文档

[English](README.md) | [仓库 README](../README.cn.md)

当前产品仅生成源码：头文件与本地配置 → 解析/分析/生成 → C#/bridge/manifest；
消费者负责编译、链接、打包、部署。CLI 与普通 C# SDK 共用协调器 `BindingHost`。

## 入门

1. [快速开始](getting-started.cn.md)：生成与外部消费。
2. [配置](configuration-guide.cn.md)与[准确字段参考](generated/config-reference.cn.md)。
3. [包](packages.cn.md)：完整 SDK、Core 接口扩展、Runtime 和 CLI。
4. [公开 API](api.cn.md)：普通类库调用、会话、检查与工作区。

## 设计与扩展

- [架构](architecture.cn.md)与[七项评估](assessment.cn.md)：中立核心与实现扩展工程及真实边界。
- [能力](capabilities.cn.md)、[ABI 安全](abi-safety.cn.md)、[生命周期](runtime-lifetime.cn.md)。
- [插件](extensibility.cn.md)、[C++ 手册](cpp-extension-cookbook.cn.md)、[lowering](lowering.cn.md)。
- [平台扩展](platform-extension.cn.md)及 [CustomTargetPlugin](../examples/CustomTargetPlugin/README.md)。
- [callback](callbacks.cn.md)、[命名](naming.cn.md)、[源码处理](source-processing.cn.md)。
- [外部编译/原生元数据](native-metadata.cn.md)、[诊断](diagnostics.cn.md)。

## 验收与历史

[测试](testing.cn.md)、[核心验收](acceptance.cn.md)、[发布验证](release-verification.cn.md)
和[发布](publish.cn.md)分别记录必需基线、安装与公开发布证据。
必需 CI 为 Windows 完整、Linux/macOS smoke 和一个完整产品安装任务；原性能预算不变。
Linux 作业执行本轮固定 Browser Mono Wasm/AOT aggregate 与回调回归一次；其他应用验证按明确范围记录。

[路线](roadmap.cn.md)说明当前收敛范围。[历史发布](releases/README.md)、旧计划和旧检查点
只证明各自原始 revision，不是当前 API、兼容保证或已完成认证。
