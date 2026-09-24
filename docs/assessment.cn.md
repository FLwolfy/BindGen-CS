# BindGen-CS 工程成熟度审计

[English](assessment.md) | [能力矩阵](capabilities.cn.md) | [验收规范](acceptance.cn.md) | [路线图](roadmap.cn.md)

审计日期：2026-09-23。

## 结论

BindGen-CS 已经是一个**在声明语义范围内很强的维护候选 C/C++ → C# 工具链**，不是简单的 header-to-`DllImport` 脚本。唯一 C# 路径已经 IR-native；高级桌面 C++ lowering、lifetime contract、真实 native invocation、确定性 packaging、API/dependency gate 与 OIDC 签名发布 workflow 均已实现。

当前 `macos-arm64-darwin` 完整报告的十个强制分类均已达到 9.0/10。

但当前 revision 还不能称为“所有桌面平台生产支持”。Windows x64、Linux x64、macOS x64 必须分别生成同版本完整报告；Windows 还必须证明 clang-cl 与 MSBuild runtime invocation。真实 OIDC/Sigstore 签名仍只能由 release runner 提供。下游项目自行拥有 binding 配置与集成验收。

## 量化判断

| 维度 | 当前判断 | 证据与扣分原因 |
| --- | ---: | --- |
| 已支持 C ABI 正确性 | 代码质量 9.0/10 | 真实库、layout、compile、invocation、snapshot 与严格诊断；desktop-x64 报告仍待完成 |
| 已支持 C++ Bridge 子集 | 代码质量 9.0/10 | lifecycle、继承 adjustment、specialization、所需 STL lowering、smart pointer、callback 均有测试；未知语义 fail-closed |
| 易用性 | 9.0/10 | `init → doctor → validate → generate → build`、workspace、schema、explain、配置相对路径和事务输出 |
| 可扩展性 | 9.3/10 | 最终 lowering contract、声明式 recipe、type/callable/artifact plugin SPI、显式 C shim、安全策略、确定性优先级、隔离与 cache fingerprint |
| 性能与确定性 | 9.0/10 | 10,000 declarations 冷/热预算、内容寻址缓存、并发发布、删除输出后恢复和稳定 hash |
| 架构完成度 | 9.0/10 | IR-native raw/friendly 是唯一路径；预发布 fallback 与旧配置迁移已删除 |
| 全球跨平台证据 | 尚未验收 | Windows x64、Linux x64、macOS x64 同版本报告仍是硬门槛 |
| 任意 C++ 语言覆盖 | 明确有边界 | 受控子集很强；任意模板元编程和未声明 allocator/container 语义会被拒绝 |
| 发布供应链 | 本地自动化 9.0/10；签名待执行 | deterministic NuGet、native-RID consumer、SPDX/SLSA、API/license/vulnerability gate；OIDC workflow 就绪，真实签名需要授权 release run |

这些分数不进行平均后冒充发布分数。发布 gate 仍采用[验收规范](acceptance.cn.md)的规则：每个声明 target 的每一个分类都必须单独达到 9.0。

## 本轮五项工作的真实状态

| 功能 | 状态 | 已有验收 | 尚需完成才可关闭 |
| --- | --- | --- | --- |
| C# 路径完全 IR-native | 完成 | raw ABI、string/span/ref/out friendly、fail-closed、last-good、schema/init tests；旧 emitter 已删除 | 持续扩大真实库 API snapshots |
| 最终 C++ lowering 架构 | 已完成 | recipe 真实调用、外部 plugin E2E、显式 shim build/export/invocation、managed/native artifact、API-shape、确定性排序、重复拒绝、built-in 同 registry | 持续扩展当前 contract；不存在预发布 adapter compatibility |
| Native build providers 与 export inspection | 桌面产物编排完成 | provider/export gates；NuGet `runtimes/<rid>/native` 与 SHA-256 index | 在 Windows 执行 clang-cl/MSBuild runtime gate |
| Desktop-x64 验收 | 自动化已就绪、证据待产出 | 独立 GNU/MSVC/Darwin x64 runner 与隔离报告 | 三个 job 必须在同一 revision 通过；dry-run/cross compile 不能计分 |
| 增量缓存、大项目预算、稳定插件契约 | 当前预发布 contract 已完成 | fingerprint、原子 immutable cache、并发/10k/plugin E2E 和 reviewed API baseline | Workspace DAG、共享 parser cache 和内存趋势属于后续扩展 |

## 为什么它已经很强

- 正确性优先：无法证明 ownership、allocator、callback lifetime 或 C++ lowering 时给出稳定诊断，而不是生成看似能编译的错误 ABI。
- 工程闭环：生成、编译、native invocation、API snapshot、确定性 package、clean native consumer 与供应链策略属于同一验收流程。
- 所有高风险能力都通过通用 config、mapping、lowering recipe/plugin/shim、provider 与 gate 表达，没有 native-library-name 特判。
- 扩展不会污染核心：第三方 lowering plugin 使用版本化 contract；其二进制、版本、shim 内容和状态参与缓存键。
- 目标隔离：target、ABI、triple、sysroot、compiler、生成输出与 snapshot/report 绑定，避免一个宿主通过被误写成另一个 ABI 已验证。

## 达到“超级通用”的硬性剩余门槛

1. 在 Windows x64、Linux x64、macOS x64、随后 Windows Arm64 执行同等级完整矩阵；Windows 还要真实执行 clang-cl/MSBuild provider。
2. 每个 production target 保留独立当前版本 acceptance artifact；Android/iOS/FreeBSD 是正式支持目标，在实现与验收闭环前标记为 ⚠️。
3. 在授权 release job 中真实执行 OIDC/Sigstore workflow，不以本地未签名 provenance 代替。
4. workspace DAG/shared parser cache 属于后续性能演进，不阻塞当前声明的单 workspace 能力。

因此最准确的产品结论是：**BindGen-CS 在显式 C/C++ contract 内已经架构清晰、功能强且易用；通用维护发布状态仍需要三份 desktop-x64 报告与一次真实签名 release run。**
