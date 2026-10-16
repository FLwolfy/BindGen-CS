# BGCS 3.0 文档

[English](README.md) | [简体中文](README.cn.md) | [仓库 README](../README.cn.md)

BGCS 3.0 使用 .NET 10、分组版本化配置和公开的 `BindingHost` 流水线。
当前实现与验收状态以[实施状态](implementation-status.cn.md)、
机器可读的[功能 inventory](../eng/acceptance/feature-inventory.json)和实际验证收据为依据。
本地部分检查成功不代表已经达到发布条件。

## 首次接入

1. [快速开始](getting-started.cn.md)：输入 header、生成 bindings、声明原生构建输入并消费实例 API。
2. [配置指南](configuration-guide.cn.md)：分组默认值、路径、语义契约、扩展和输出模式。
3. [生成配置参考](generated/config-reference.cn.md)：当前 schema 的全部属性、约束与默认值。
4. [包与分发](packages.cn.md)：完整 SDK、Contracts-only 插件、Runtime 消费工程和工具实体分发。
5. [MSBuild 集成](msbuild.cn.md)：显式启用、精确 manifest 输入和独立 inner build。

## 架构与扩展

| 任务 | 文档 |
| --- | --- |
| 理解模块职责、依赖和流水线 | [架构](architecture.cn.md) |
| 嵌入生成、构建、检查或工作区 | [公开 API](api.cn.md) |
| 声明原生源码、选择 provider 并消费验证产物 | [原生构建](native-build.cn.md) |
| 理解资源、回调 drain 和异步取消 | [Runtime 生命周期](runtime-lifetime.cn.md) |
| 配置同步回调、返回 handle 的注册与终态异步操作 | [回调实战手册](callbacks.cn.md) |
| 分清存储、调用 ABI 和语义证据 | [ABI 安全](abi-safety.cn.md) |
| 从精确 raw 签名读取原始函数、类型和参数名称 | [原生名称属性](native-metadata.cn.md) |
| 编写中立插件 | [扩展](extensibility.cn.md) |
| 理解诊断与处理方法 | [诊断](diagnostics.cn.md) |
| 添加 C++ 类型 / 调用 lowering 或原生 shim | [C++ 实战手册](cpp-extension-cookbook.cn.md)、[lowering 架构](lowering.cn.md) |
| 查看分组 C++ 配置 | [生成 C++ 配置参考](generated/cpp-config-reference.cn.md) |
| 理解宿主、目标与 SDK 分离 | [平台扩展](platform-extension.cn.md) |
| 查看设计决策 | [架构决策记录](adr/) |

## 验证与发布

- [测试](testing.cn.md)说明必需的原生、NativeAOT、浏览器 Wasm 和仓库验收执行。
- [包认证](packages.cn.md#打包与验收)安装真实本地产物，编译并调用消费工程，检查 schema 闭包并比较重复打包内容。
- [发布](publish.cn.md)说明同 revision 发布门禁、产品包和实际授权发布流程。
- [旧配置选项处置依据](parity-option-dispositions.md)记录精确旧实现调用关系与显式托管展示选择。
- [贡献](../CONTRIBUTING.md)记录仓库开发要求。
- [Release 历史](releases/README.md)保留历史版本可用性与版本记录。

目标解析、头文件解析、生成、托管编译、原生链接、实际调用和包消费是独立阶段。
每项支持声明都需要当前收据注明准确的目标、运行时、导入方式、工具链和 source revision。
历史报告与旧成熟度评分属于历史证据，不能证明 3.0 实现通过验收，也不能代替必需发布门禁。
