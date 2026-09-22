# BindGen-CS 中文 Wiki

[English](README.md) | [简体中文](README.cn.md) | [仓库 README](../README.cn.md)

## 使用指南

1. [快速开始](getting-started.cn.md)
2. [配置指南](configuration-guide.cn.md)
3. [C# API 参考](api.md)
4. [NuGet 包与导出 API](packages.cn.md)
5. [C++ Bridge 配置](cpp2c.config.md)
6. [测试说明](testing.md)
7. [发布说明](publish.md)

## 设计与质量

- [架构说明](architecture.cn.md)
- [验收规范](acceptance.cn.md)

## 稳定功能与进行中功能

七层架构、共享 IR、C#/Runtime/CBridge emitter、CLI/workspace 工作流、安全输出事务、SingleFile、target/toolchain model、NuGet 闭包、真实库性能预算、按 target 维护的确定性 snapshot、STL/smart-pointer lowering、managed virtual callback proxy、ownership diagnostics，以及 InnoEngine 五项目自动绑定 gate 均已可用。每份生成的验收报告是某一 target 实际通过范围的权威记录。
