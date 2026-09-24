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
| 中大型 C API | 9.0 | SDL3、miniaudio、cimgui、cimguizmo、bgfx 确定性 snapshot 与 IR-native 零警告编译 |
| 复杂 C ABI 正确性 | 9.0 | 宿主原生 invocation 与 target-specific ABI/layout/调用约定矩阵 |
| 普通 C++ class bridge | 9.0 | lifecycle/method 原生编译、链接和运行 |
| 复杂现代 C++ | 9.0 | 选定模板、STL、智能指针、virtual callback |
| 生成 API 美观程度 | 9.0 | 源码/public API 快照、analyzer、生成目录外零 native import |
| 小白易用性 | 9.0 | 从 header 到验证输出最多五条命令 |
| 外层架构 | 9.0 | Intermediate/Runtime dependency-boundary tests 与完整 solution build |
| 内部架构 | 9.0 | 共享 IR、analyzer/IR-emitter tests，且没有预发布 fallback emitter |
| NuGet/测试/发布工程化 | 9.0 | clean packages/symbols、API/dependency policy、确定性输出、native-RID consumer、host-native 与 managed matrix |

## 真实库性能预算

在受控、已记录的 host 上 warm restore 后测量。普通功能矩阵记录生成耗时，但不因共享 runner 的负载或墙钟变化而失败。专用性能测试可设置 `BGCS_ENFORCE_GENERATION_BUDGETS=1` 强制以下上限；`scripts/test-performance-budget.sh` 另行验证冷/热缓存。

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
- string、vector、span、array、map、set、optional、variant、expected、path、chrono、unique/shared pointer lowering；
- allocator/deallocator pairing、retained callback unregister/drain race 与 async completion lifetime；
- 配置的 abstract callback interface 的 managed implementation；
- native bridge compile，以及 synthetic lifecycle/method runtime invocation。

无法支持的结构必须输出可执行诊断和可检查报告；静默生成 ABI 不安全签名直接判定失败。

## API 美观证据

- 生成文件绝不手工修改。
- 库特定行为必须放在 preset、policy 或生成目录外的 patch 中。
- public API snapshot 必须审阅并编译。
- C# 源码 snapshot 除文件头的 `ABI reference target` 标记外逐字节比较；该标记只记录解析参考目标，不代表 binding 契约变化。其他空白或正文变化仍须审阅并更新基线。
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
- 五个真实 C 库全部通过 IR-native backend 重生成并以 warning-as-error 编译；不完整 opaque storage 的按值传递会明确拒绝而不是猜测。

主配置路径从 canonical IR 调用 `CSharpEmitter`，覆盖 raw 与 string/span/ref/out friendly surface。不存在预发布 compatibility emitter 或旧 schema migration path。

## 包和发布证据

- 全部 package ID 使用同一版本。
- 干净 local feed 可以恢复 public package 和全部传递实现包。
- 消费者使用隔离 package cache；已经由 solution restore 锁定的第三方包只从本机 global-packages fallback 读取，发布 smoke 不依赖 nuget.org 在线可用性。
- Tool 安装到空 tool path，并完成 init/generate/build smoke。
- 同一 commit 两次 clean pack 在排除 NuGet signature metadata 后内容等价。
- clean consumer 会从 `runtimes/<rid>/native/` 选择并调用当前 host binary。
- reviewed API baseline、dependency license、known-vulnerability query 与确定性 SPDX/SLSA payload 共同 gate 本地 release-candidate 构建。
- 正式发布还必须由 GitHub release job 获取 OIDC identity 并生成可验证 Sigstore attestation；本地只验证 workflow，不会把“工作流存在”冒充为“已执行签名”。
- symbol package 和 repository metadata 完整。
- mandatory gate 全部通过前不得发布。

## 当前状态

`scripts/run-full-test-matrix.sh` 只有在上述本地 BGCS gates 全部通过后，才写入 `artifacts/acceptance/report.json`、`report.md` 与 target-specific 副本。维护候选必须具备 Windows x64 MSVC、Linux x64 GNU、macOS x64 Darwin 三份报告；Windows 还必须真实执行 clang-cl/MSBuild DLL build/export/invocation。三份同版本报告在 runner 完成前均保持 pending。具体消费者的配置、生成与集成验收归消费者仓库所有，不计入 BGCS 分数。真实 OIDC/Sigstore 签名同样属于 release runner 产物，不是本地证据。
