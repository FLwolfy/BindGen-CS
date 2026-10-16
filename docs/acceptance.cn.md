# 验收规范

[Wiki](README.cn.md) | [English](acceptance.md)

本文定义强制验收证据。结果报告明确 gate 的通过或失败，不把测试结果换算为代码质量评分。

## 总规则

每个分类都有明确的自动验收 gate。只有全部 gate 成功，该分类才通过；缺少任何 gate 时报告生成失败。报告按 target 隔离，通过只证明 target 字段声明的平台、架构与 ABI。

发布流程必须生成：

- `artifacts/acceptance/report.json`：机器可读结果；
- `artifacts/acceptance/report.md`：人工审阅摘要。

两份报告必须记录 UTC 生成时间、Git revision 和 working-tree dirty 状态，使本地未提交验收与发布 commit 验收可以明确区分。dirty 报告可以作为开发证据，但不能冒充某个不可变 release commit 的证据。

## 目标矩阵

| 方面 | 必要结果 | 强制 gate |
| --- | ---: | --- |
| 普通小型 C API | passed | 生成编译率 100%、ABI 调用、生成源码零手改 |
| 中大型 C API | passed | SDL3、miniaudio、cimgui、cimguizmo、bgfx 确定性 snapshot 与 IR-native 零警告编译 |
| 复杂 C ABI 正确性 | passed | 宿主原生 invocation 与 target-specific ABI/layout/调用约定矩阵 |
| 普通 C++ class bridge | passed | lifecycle/method 原生编译、链接和运行 |
| 复杂现代 C++ | passed | 选定模板、STL、智能指针、virtual callback |
| 生成 API 美观程度 | passed | 源码/public API 快照、analyzer、生成目录外零 native import |
| 小白易用性 | passed | 从 header 到验证输出最多五条命令 |
| 外层架构 | passed | Contracts/Runtime 独立性、Contracts-only 插件和完整 solution build |
| 内部架构 | passed | 十三个工程的依赖门禁、中立模型分层、worker 隔离及无旧 facade/configuration 路径 |
| NuGet/测试/发布工程化 | passed | clean packages/symbols、API/dependency policy、确定性输出、native-RID consumer、host-native 与 managed matrix |

## 真实库性能预算

在受控、已记录的 host 上 warm restore 后测量。真实库功能矩阵记录生成耗时，但不因共享 runner 的负载或墙钟变化而失败；专用真实库性能测试可设置 `BGCS_ENFORCE_GENERATION_BUDGETS=1` 强制以下上限。`scripts/test-performance-budget.sh` 则独立强制下文的 10,000 声明冷/热缓存预算。

10,000 声明的缓存验收检查生成方法数、删除输出目录后的逐字节一致性，以及完整且带封印的不可变产物集合。当前 `eng/acceptance/performance-budgets.json` 规定 Linux、Windows x64 冷 60 秒／热 10 秒，macOS Intel 冷 120 秒／热 30 秒；ARM64 预算属于独立认证。`BGCS_PERF_COLD_BUDGET_SECONDS` 和 `BGCS_PERF_WARM_BUDGET_SECONDS` 可以设置更严格上限；报告记录实际门限、worker 与会话峰值内存。C# emission 缓存命中本身不能证明解析器或整条流水线复用。

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
- UTF-8/UTF-16/UTF-32 string、显式字节或 code-unit 长度、内嵌零及 borrowed/owned/caller-allocated 生命周期；
- 分别验证原生布局、托管存储和按值调用兼容性，包括过度对齐和 long double 的 pointer/shim 替代路径；
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
- 实际 native bridge 编译及托管消费者的 lifecycle/method 调用。

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

`doctor` 报告工具链及资源问题；`validate` 检查配置与中立契约，不替换输出。`generate` 使用统一 C/C++ 流水线，按需自动生成 C++ bridge。`build` 在联合发布前编译候选托管源码，并执行配置要求的 native sources/prebuilt 验证。`bridge` 和 `native-build` 保留为同一 Host 的显式请求。安全或 C++ 拒绝诊断应指出具体配置或源码修复方式。

`nativeBuild.mode: none` 不请求构建产品原生库，但不跳过源码预处理或所需的独立原生事实证明。
例如，使用 GNU SDK 声明 prelude 的解析结果要求在发布前由所选原生编译器独立检查类型、布局和常量，
该检查也适用于 `generate` 和 `bridge`。报告必须分别记录产品构建/链接是否执行，以及
`nativeFactProofRequired`、`nativeFactProof` 的实际结果；事实证明不能冒充产品链接或调用证据。

## 架构证据

当前自动架构测试直接保证：

- Runtime 和 Contracts 各自不引用其他 BGCS 程序集；
- Contracts 不泄漏 frontend、Roslyn、filesystem、MSBuild 或 CLI 类型；
- 标准 lowering 和 C# emission 不引用解析器实现；
- 默认 Host 通过受版本约束的 worker 进程边界取得已脱离原生句柄的 NativeModule；
- NativeModule、CppBridgeModule、InteropModule、ManagedApiModel 与 CSharpEmissionPlan 各有职责和独立测试；
- C# backend 在发布前复核计划与实际 import/storage 契约，拒绝未知按值 ABI；
- 真实 C 语料与 bimg C++ 路径重新生成并通过 warnings-as-errors 编译，不手改生成代码；
- 禁止生产源码 Compile Link 共享及生产程序集友元。唯一登记的 frontend 测试友元必须指向真实、不可打包的测试项目。

CLI、嵌入式 SDK、工作区和 MSBuild 调用公开 BindingHost 流水线。CSharpBackend 消费 managed/interop 契约及经过检查的 emission plan。不存在旧 CsCodeGenerator facade、BindingModule、平铺配置读取器、迁移工具或兼容 emitter。中立模型扩展端口和仅限 worker 的高级 Clang 端口分别说明。

## 包和发布证据

- 全部 package ID 使用同一版本。
- 干净 local feed 可以恢复四个产品及声明的 parser/tool 技术分包；内部工程不作为用户逐项安装的产品。
- 消费者使用隔离的产品 cache 和精确归档 hash。允许的第三方 fallback 不得替代已安装 BGCS 产品。正式发布另需从实际公共 NuGet 源安装验证。
- Runtime 包与源码消费者覆盖最小和完整源码闭包、单文件、独立 namespace，且不引用生成器。
- Tool 安装到空 tool path，并完成 init/generate/build smoke。
- 同一 commit 两次 clean pack 在排除 NuGet signature metadata 后内容等价。
- clean consumer 会从 `runtimes/<rid>/native/` 选择并调用当前 host binary。
- reviewed API baseline、dependency license、known-vulnerability query 与确定性 SPDX/SLSA payload 共同 gate 本地 release-candidate 构建。
- 正式发布还必须由 GitHub release job 获取 OIDC identity 并生成可验证 Sigstore attestation；本地只验证 workflow，不会把“工作流存在”冒充为“已执行签名”。
- symbol package 和 repository metadata 完整。
- mandatory gate 全部通过前不得发布。

## 当前状态

`scripts/run-full-test-matrix.sh` 仅在必需门禁成功后写入宿主验收报告。完整发布要求来自 `eng/acceptance/required-release-gates.json`：同 revision 的桌面报告、浏览器解释执行/AOT、NativeAOT、完整产品安装与 Visual Studio 2026 TaskHost 证据。Host 测试使用精确结构化发现、八个互斥的类分片及完整 raw/TRX 并集；缺失、额外、重复、跳过或失败的用例均不能认证整套测试。dirty 本地收据仍是开发证据。参见[审核与认证状态](release-evidence-state-flow.md)；经过身份验证的发布前置检查、实际签名和公共源安装不能由仓库内的完成声明替代。
