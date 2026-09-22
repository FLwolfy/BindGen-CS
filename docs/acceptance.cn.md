# 验收规范

[Wiki](README.cn.md) | [English](acceptance.md)

本文是规范性文档。评分必须由测试产物生成，维护者不得手工指定分数。这里的 9.0 是 BindGen-CS 自己定义的 release-gate 等级，不是外部行业基准、完整 C++ 覆盖率或第三方审计分数。

## 总规则

每个分类都有明确的一组自动 mandatory gate。只有这些 gate 在发布 commit 上全部通过，该分类才得到 **9.0/10.0**；缺少任何 gate 时，报告生成直接失败。报告按 target 隔离：通过只证明 `target` 字段声明的平台、架构和 ABI。

发布流程必须生成：

- `artifacts/acceptance/report.json`：机器可读结果；
- `artifacts/acceptance/report.md`：人工审阅摘要。

两份报告必须记录 UTC 生成时间、Git revision 和 working-tree dirty 状态，使本地未提交验收与发布 commit 验收可以明确区分。dirty 报告可以作为开发证据，但不能冒充某个不可变 release commit 的证据。

## 目标矩阵

| 方面 | 目标 | 强制 gate |
| --- | ---: | --- |
| 普通小型 C API | 9.0 | 生成编译率 100%、ABI 调用、生成源码零手改 |
| 中大型 C API | 9.0 | SDL3、miniaudio、cimgui、cimguizmo、bgfx 重新生成/编译及 InnoEngine workspace diff |
| 复杂 C ABI 正确性 | 9.0 | 宿主原生 invocation 与 target-specific ABI/layout/调用约定矩阵 |
| 普通 C++ class bridge | 9.0 | lifecycle/method 原生编译、链接和运行 |
| 复杂现代 C++ | 9.0 | 选定模板、STL、智能指针、virtual callback |
| 生成 API 美观程度 | 9.0 | 源码/public API 快照、analyzer、生成目录外零 native import |
| 小白易用性 | 9.0 | 从 header 到验证输出最多五条命令 |
| 外层架构 | 9.0 | Intermediate/Runtime dependency-boundary tests 与完整 solution build |
| 内部架构 | 9.0 | 共享 IR、analyzer/IR-emitter tests，以及兼容迁移状态的明确记录 |
| NuGet/测试/发布工程化 | 9.0 | 干净包、symbols、确定性输出、宿主原生与 managed 矩阵 |

## 真实库性能预算

在报告声明的宿主 target、NuGet 已 warm restore 的条件下测量：

| 库 | 生成预算 | 强制结果 |
| --- | ---: | --- |
| cimguizmo | 15 秒 | C Bridge 和 C# 编译 |
| cimgui | 30 秒 | C# 编译和 API 快照 |
| SDL3 | 45 秒 | C# 编译和 API 快照 |
| miniaudio split header | 60 秒 | C# 编译和 API 快照 |
| bgfx C99 | 30 秒 | C# 编译和 API 快照 |
| bimg C++ | 15 秒 | C Bridge、原生语法检查、C# 回绑和 API 快照 |

超时、OOM、无限 cache 增长或为了通过测试手工删除声明，都判定中大型 C 分类失败。

## ABI 证据

ABI 测试必须使用同一组头文件编译 native test DLL，并比较 managed 观察值：

- primitive size 和 signedness；
- enum underlying type；
- sequential、explicit、packed、nested、anonymous、aligned record；
- 一维/多维 fixed array 和 flexible array tail；
- signed、unsigned、zero-width、cross-unit、struct/union bitfield；
- pointer、reference、function pointer、callback、user data；
- cdecl、stdcall、适用时的 thiscall，以及 vectorcall diagnostics；
- borrowed、owned、caller-allocated 的 UTF-8/UTF-16 string；
- pointer/count、capacity/written-count、two-call query pattern。

只有生成代码编译成功、没有 native invocation，不算 ABI 证据。

## C++ 证据

C++ Bridge 必须编译、链接并执行以下测试：

- class lifecycle、instance/static/overloaded method；
- namespace free function 与异常边界；
- multiple inheritance 与生成的 pointer adjustment；
- exception 捕获和 managed error 传播；
- 显式 class/function template instance；
- string、vector、span、blittable/non-blittable optional、unique/shared pointer adapter；
- 配置的 abstract callback interface 的 managed implementation；
- native bridge compile，以及 synthetic lifecycle/method runtime invocation。

无法支持的结构必须输出可执行诊断和可检查报告；静默生成 ABI 不安全签名直接判定失败。

## API 美观证据

- 生成文件绝不手工修改。
- 库特定行为必须放在 preset、policy 或生成目录外的 patch 中。
- public API snapshot 必须审阅并编译。
- 保留 raw imports；在 ownership 事实充分时，友好 API 使用 span、string、handle、result 和确定性命名。
- 生成源码必须通过 warning-as-error 编译；文档和 lifetime 信息只在 native 声明或显式配置提供事实时生成。

## 小白流程证据

下面流程必须能在空目录成功：

```bash
bindgen-cs init path/to/header.h
bindgen-cs doctor
bindgen-cs validate
bindgen-cs generate
bindgen-cs build
```

`doctor` 报告工具链问题；`validate` 只解析和验证 IR，不替换输出；`build` 编译生成 C#。`bridge` 生成带版本的 native build manifest，`native-build` 可使用内置 Clang/GNU-compatible provider 编译它；普通 C# `build` 仍保持独立。结构化 safety/C++ rejection 必须提供建议的配置路径或源码操作。

## 架构证据

当前自动架构测试直接保证：

- Runtime 不依赖 generator；
- Intermediate 不依赖 facade、emitter、Roslyn、filesystem 或 CLI；
- Intermediate assembly 不引用其他 BGCS assembly；
- IR analyzer 与 IR-native `CSharpEmitter.Emit` 有独立行为测试；
- IR-native C# 不支持的语义会在写输出前以稳定 `BGCSCS001` 失败。

主配置路径仍调用隔离的 `AstGenerationStepEmitter` 和 `GenerationStep`；`CSharpEmitter` 已不再包含或公开 legacy 路径。因此架构 9.0 表示当前声明的 boundary/build/test gate 通过，不表示默认路径 IR migration 已结束。完成标准见[架构说明](architecture.cn.md#迁移完成条件)。

## 包和发布证据

- 全部 package ID 使用同一版本。
- 干净 local feed 可以恢复 public package 和全部传递实现包。
- 消费者使用隔离 package cache；已经由 solution restore 锁定的第三方包只从本机 global-packages fallback 读取，发布 smoke 不依赖 nuget.org 在线可用性。
- Tool 安装到空 tool path，并完成 init/generate/build smoke。
- 同一 commit 两次 clean pack 在排除 NuGet signature metadata 后内容等价。
- symbol package 和 repository metadata 完整。
- mandatory gate 全部通过前不得发布。

## 当前状态

macOS arm64 Darwin 范围的全部测量分类均达到 9.0。`scripts/run-full-test-matrix.sh` 只有在 managed tests、native C/C++ runtime gate、五个真实 C 库、bimg C++ Bridge、确定性源码/reflection 快照、InnoEngine 五项目 workspace/native dependency/build/native-test gate 以及 NuGet/Tool smoke 全部通过后，才写入 `artifacts/acceptance/report.json` 与 `report.md`。Windows 和其他 ABI 必须分别生成 target-specific 报告；任何报告都不能被当作其他 target 的证明。
