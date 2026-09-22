# BindGen-CS 工程成熟度审计

[English](assessment.md) | [能力矩阵](capabilities.cn.md) | [验收规范](acceptance.cn.md) | [路线图](roadmap.cn.md)

审计日期：2026-09-22。

## 结论

BindGen-CS 已经是一个**在已声明范围内达到生产级的 C/C++ → C# 工具链**，不是简单的 header-to-`DllImport` 脚本。当前 `macos-arm64-darwin` 完整验收的十个分类均达到 9.0/10.0；InnoEngine 的五个 native binding 项目可以配置驱动地重生成，并通过 native dependency、managed solution、native binding tests 和手写 import 审计。

但它目前还不能诚实地称为“任意 C++、任意平台、零配置”的超级万能工具。目标范围内的 9.0 验收和全球平台/完整语言成熟度是两个不同指标：后者仍受默认 C# 路径尚未完全 IR-native，以及 Windows/Linux 缺少本次代码版本的同等级实机报告所限制。

## 量化判断

| 维度 | 当前判断 | 证据与扣分原因 |
| --- | ---: | --- |
| 已支持 C ABI 正确性 | 9.0/10 | 真实库、layout、compile、invocation、snapshot 和严格诊断均进入 macOS gate |
| 已支持 C++ Bridge 子集 | 9.0/10 | class/lifecycle、继承 adjustment、显式模板、选定 STL、smart pointer、callback proxy 有测试；未知语义 fail-closed |
| 易用性 | 9.0/10 | `init → doctor → validate → generate → build`、workspace、schema、explain、配置相对路径和事务输出 |
| 可扩展性 | 9.1/10 | v1 plugin contract、type/callable adapter SPI、确定性优先级、隔离依赖、原子注册和 cache fingerprint |
| 性能与确定性 | 9.0/10 | 10,000 declarations 冷/热预算、内容寻址缓存、并发发布、删除输出后恢复和稳定 hash |
| InnoEngine 自动化 | 9.0/10 | 五项目 diff、全部 native dependency、solution build、六个 native test project 和手写 import audit |
| 架构完成度 | 8.4/10 | 目标分层清晰，`CSharpEmitter` 已只消费 IR；默认输出仍由隔离的 `AstGenerationStepEmitter` 承担部分语义 |
| 全球跨平台证据 | 7.0/10 | target/ABI/provider/CI 已建模；只有 macOS arm64 具备本次版本的完整实机 acceptance artifact |
| 任意 C++ 语言覆盖 | 7.5/10 | 受控子集很强；任意模板元编程、allocator/container 组合不会被猜测性生成 |
| 发布供应链 | 8.6/10 | deterministic NuGet、干净消费者和 tool workflow 已通过；SBOM/provenance/正式兼容窗口尚未完成 |

这些分数不进行平均后冒充发布分数。发布 gate 仍采用[验收规范](acceptance.cn.md)的规则：每个声明 target 的每一个分类都必须单独达到 9.0。

## 本轮五项工作的真实状态

| 功能 | 状态 | 已有验收 | 尚需完成才可关闭 |
| --- | --- | --- | --- |
| 默认 C# 路径完全 IR-native | 进行中 | `CSharpEmitter` 无 AST 依赖、unsupported IR fail-closed、架构测试 | 把 constant、delegate、alias、bitfield、handle、friendly overload 与 import-mode 语义完整放入 canonical IR；默认路径删除 `AstGenerationStepEmitter` |
| 正式 C++ type/callable adapter SPI | 已完成 v1 | 外部 assembly E2E、API-shape、确定性排序、重复拒绝、built-in 同 registry、ctor/dtor/static/instance/free 覆盖 | 后续新增 adapter 只能扩展，不得破坏 v1 contract |
| Native build providers 与 export inspection | 核心完成 | direct Clang/GNU 与 CMake 在 macOS 实编译并核验导出；clang-cl/Meson/MSBuild 有确定性 plan tests | 在所属 Windows/Linux target 执行 provider runtime gate；补 multi-RID artifact layout |
| Windows/Linux 与 macOS 同级验收 | 未关闭 | CI job、target 隔离、Windows snapshots 与 managed model 已存在 | 在 Windows/Linux 本机生成当前 commit 的完整报告；不允许用 dry-run 或 cross compile 冒充 runtime pass |
| 增量缓存、大项目预算、稳定插件契约 | 已完成 v1 | compiler/plugin/adapter/input fingerprint、原子 immutable cache、并发测试、10k 冷/热 gate、外部 plugin E2E | Workspace DAG、共享 parser cache、内存趋势和 obsolete window 属于下一版扩展，不否定 v1 完成 |

## 为什么它已经很强

- 正确性优先：无法证明 ownership、allocator、callback lifetime 或 C++ lowering 时给出稳定诊断，而不是生成看似能编译的错误 ABI。
- 工程闭环：生成、编译、native invocation、API snapshot、package consumer 和真实引擎集成属于同一验收流程。
- 没有 native-library-name 特判：InnoEngine 的需求通过配置、通用 mapping、adapter、provider 和 gate 表达。
- 扩展不会污染核心：第三方插件和 C++ adapter 使用版本化 contract；其二进制、版本和状态参与缓存键。
- 目标隔离：target、ABI、triple、sysroot、compiler 与 snapshot/report 绑定，避免“在 macOS 生成成功”被误写成 Windows/Linux 已验证。

## 达到“超级通用”的硬性剩余门槛

1. 完成 canonical IR 对默认 C# surface 的等价表达，并删除默认 AST compatibility emission。
2. 在 Windows x64 与 Linux x64 执行相同的完整矩阵，随后再扩展 arm64、Android、iOS 和 FreeBSD。
3. 扩大经过 native 验证的 C ABI 组合，以及 `map/set/array/variant/expected/path/chrono` 等 adapter；仍不得猜测未知 allocator/lifetime。
4. 完成 multi-RID native asset layout、workspace DAG/parallelism、正式 plugin obsolete window、SBOM 和 provenance。
5. 每个新增 production target 都必须保留独立、当前版本生成的 acceptance artifact。

因此最准确的产品结论是：**BindGen-CS 在 macOS 与当前明确支持的 C/C++ 子集内已经非常优秀、功能强且可用于真实大型工程；它正在成为超级通用工具，但在 IR 迁移和 Windows/Linux 实机证据完成前，不应宣称已经全平台万能。**
