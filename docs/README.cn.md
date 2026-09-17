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

七层架构、共享 IR、C#/Runtime/CBridge emitter、CLI 工作流、安全输出事务、SingleFile、NuGet 闭包、真实库性能预算和确定性 API snapshot 已经可用。高级 STL/smart-pointer lowering、managed virtual callback proxy、ownership annotation 和四个上游 native DLL 的直接调用仍属于进行中功能。
