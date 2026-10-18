# 包与安装消费

[English](packages.md) | [快速开始](getting-started.cn.md) | [发布](publish.cn.md)

| 产品 | 用途 |
| --- | --- |
| BGCS | 完整嵌入式 C/C++ 源码生成 SDK |
| BGCS.Contracts | 中立模型与普通插件契约 |
| BGCS.Runtime | 不依赖生成器/parser 的绑定消费支持 |
| BindGen-CS | 使用相同 `BindingHost` 协调器的 CLI |

全部产品使用 .NET 10。C++ lowering 等内部模块包含在完整 SDK/工具中，不需按工程
安装功能包。实现共十一个产品工程，不交付 BGCS MSBuild 集成资产。

## 安装与生成

声明版本实际发布后：

```bash
dotnet tool install --global BindGen-CS --version 3.0.0
bindgen-cs init include/native.h --language c
bindgen-cs doctor bindgen.json
bindgen-cs generate bindgen.json
```

发布前使用源码或显式准备的本地 feed。CLI 和普通 C# SDK 调用生成 C#、按需 C ABI
bridge 源码、manifest 和诊断；消费者负责编译、链接、打包、部署。产品没有原生构建
或消费者构建 API。见[公开 API](api.cn.md)。

## Worker 资源与 SDK 部署

parser 资源跟随生成器宿主，不跟随生成目标。Windows→Android 使用 Windows parser
和真实 Android SDK 输入；Clang builtin headers 不替代目标 SDK/libc/C++ 头文件。

SDK 通过标准 NuGet `contentFiles` 交付锁定的 worker/resource payload；本地首次使用
按 version/host/manifest digest 选择所属缓存、验证来源及缓存闭包，再准备 worker，不依赖 BGCS `.props`/`.targets`、TaskHost 或运行时搜索
NuGet 全局缓存。Restore 必须交付完整内容。普通 Contracts-only 插件不加载 Clang，
高级事实贡献器在 worker 运行。

工具 pointer/RID 实现包提供声明的六种桌面宿主闭包，技术性 Intel macOS supplement
补齐上游资源缺口。产品、RID、archive 集合与大小限制以
[product-packages.json](../distribution/manifests/product-packages.json)为准，不使用历史固定任务数。
parser 来源、许可证和 hash 证据仍需保留。

## Runtime 包或源码

生成应用通常只引用 `BGCS.Runtime`；`runtime.mode = "source"` 将所需闭包发射到
API 本地 namespace，单文件模式可放入 `Bindings.cs`。Runtime 不安装编译器、不构建库、
不部署原生依赖。package/source 的 Raw ABI 和友好投影边界相同。

## 打包与认证

必需 CI 是三个生成器任务（Windows 完整、Linux/macOS smoke）和一个完整产品安装任务。
安装任务在干净私有工作区使用实际新包，检查 SDK、工具、Contracts 插件、Runtime
消费者、schema 和 worker/resource 闭包；不能用旧包或仓库工程引用替代。
核心功能基线和原有各宿主性能限制仍为必需。

AOT/Wasm 应用发布/调用是可选手动私有工作，不是强制安装矩阵；目标事实与静态 adapter
生成仍在范围内。Visual Studio TaskHost 认证不属于产品要求。

`eng/pack.py` 和 `eng/verify-packages.py` 是仓库打包/验证工具，成本较高的本地运行前
先查看当前 `--help`。partial/dirty 回执仍是本地证据。最终发布另需适用的干净 revision
检查、不可变包身份和真实公开可用性验证；本文不宣称 3.0.0 已发布或已认证。
