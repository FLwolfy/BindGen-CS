# 核心功能验收

[English](acceptance.md) | [测试](testing.cn.md) | [评估](assessment.cn.md)

验收按当前核心功能与配置基线判断通过/失败。删除的产品职责记入明确 scope removal，
不能冒充已通过功能。源码实现、本地执行、发布认证是不同状态；本文不宣称当前门禁已通过。

## 必需 CI 范围

必需计划是三个生成器任务及一个完整产品安装任务：

| 任务职责 | 必需范围 |
| --- | --- |
| Windows 生成器 | 当前完整生成器回归/基线检查 |
| Linux 生成器 | 当前生成器 smoke 及记录的宿主检查 |
| macOS 生成器 | 当前生成器 smoke 及记录的宿主检查 |
| 产品安装 | 实际完整 SDK/tool/Contracts/Runtime 包消费 |

原有各宿主性能预算全部保留。准确选定 suite、任务/回执身份由 workflows 和
[required-release-gates.json](../eng/acceptance/required-release-gates.json)定义。
不能把失败基线、被跳过的选定测试或缺少输入静默记为通过。
AOT/Wasm 应用构建/运行仅为可选手动私有检查，相关目标事实、能力拒绝和源码生成仍属产品。
Visual Studio TaskHost 与 BGCS MSBuild 集成不在产品范围内。

## 核心证据

- 公开 C 声明/类型/常量/宏、依赖闭包和事实保留。
- 普通 C ABI、布局/调用分类，以及不支持形状的明确拒绝。
- 类型化 C++ 对象/模板/家族协议、cast、callback 和异常边界。
- 三种 import、Raw/友好层、Runtime package/source 与生命周期。
- 中立插件激活、输入/指纹/缓存和 worker 隔离。
- 确定性源码/API 快照、最终契约检查及安全产物发布。
- 基线要求实际调用时的独立编译/链接/调用 fixture。
- 干净产品安装、schema/resource 闭包及依赖/许可证完整性。

生成器发射源码与断言；仓库测试可编译、链接、调用，不因此恢复产品构建流水线。
等大小、目标名称或用户断言不能升级为已验证 ABI 证据。快照改变需审阅源码/API，不能
为编译通过删掉生成声明。

## 性能预算不变

10,000 声明测试检查数量、全部产物字节和删除所属输出后的完整恢复。
Windows/Linux x64 为 cold 60 秒 / warm 10 秒，Intel macOS 为 cold 120 秒 / warm 30 秒；
worker/session 内存上限 2 GiB / 4 GiB。以
[performance-budgets.json](../eng/acceptance/performance-budgets.json)为准。
仅 emission 命中不能证明完整流水线，显式更严格预算需记录。

真实库专门预算仍为 cimguizmo 15 秒、cimgui 30 秒、SDL3 45 秒、miniaudio 60 秒、
bgfx 30 秒、bimg 15 秒。功能语料运行记录耗时，受控
`BGCS_ENFORCE_GENERATION_BUDGETS=1` 运行执行限制。OOM、输出不完整、不安全源码修补
或缺少必需检查仍为失败。

## 证据与发布

清单对应准确当前实现和可执行断言。稳定 ID、源码/API 快照与 scope-removal 记录防止
历史测试自动认证替代实现。发现了测试不等于执行了测试；回执保留 revision、源码身份、
dirty 状态、宿主、选定 case 集合和真实工具输入。本地子集不能认证不可变发布 commit
或另一个平台。

源码/API/架构检查约束十一个工程及 Contracts/Runtime 独立性；中立端口不泄露 parser
handle，生产友元/共享源码不能绕过边界，未知生命周期保持未知。

发布另外遵守适用的包、来源证明和公开可用性流程。AOT/Wasm 私有回执保持可选标记及
实际范围；workflow 配置或本文都不授予 `releaseReady`。
见[发布验证](release-verification.cn.md)、[包](packages.cn.md)和[发布](publish.cn.md)。
