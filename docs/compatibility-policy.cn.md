# 预发布兼容与未来废弃政策

[English](compatibility-policy.md) | [文档首页](README.cn.md)

BindGen-CS 尚未发布首个稳定版，因此当前产品明确**不承诺**兼容旧的预发布配置、emitter、生成源码或公开 API。

## 首个稳定版之前

- 只接受唯一当前 `ConfigVersion`；其他版本稳定失败，不做隐式迁移或 fallback emitter。
- canonical Binding IR 是唯一 C# emission 来源；已删除的原型/AST 输出路径不会藏在兼容开关后继续运行。
- public API snapshot 是 code review gate，不表示预发布 API 永远不能改；有意变更必须在同一审查中更新 baseline。
- ABI 与内存安全优先于保留原型行为。无法证明 ownership、allocator、callback、async、inheritance 或 template 语义时 fail-closed。
- 平台支持必须由同一源码 revision 的 target-specific report 证明；plan、cross compile 或其他架构报告都不能替代。

架构仍保留显式配置版本、typed diagnostics、plugin revision 握手和 API diff 自动化，以便稳定版后干净地引入 compatibility。当前只有一套 lowering contract；它不会加载或包装已删除的预发布 adapter SPI。revision 握手只是实现层保护，不是对外的“v1/v2”产品标记。

## 首个稳定版之后

首个稳定版建立初始 compatibility baseline。此后删除公开 contract 必须经过：

1. 公告、changelog、替代方案和 machine-readable diagnostic；
2. 至少两个 minor release 的 compile-time/CLI/schema deprecation；
3. 配置语法变化时提供显式 config migration command；
4. 只能在下一 major 删除，通常距离公告不少于 12 个月；
5. API diff、migration tests、clean consumers 和 target acceptance 全部通过。

安全漏洞、已证实 ABI corruption 或上游运行时强制移除可以缩短周期，但 release notes 必须提供证据与缓解方法。

当前没有 deprecation register，因为现在不存在稳定 legacy contract。

## 发布证据

`bindgen-cs supply-chain` 生成确定性的 SPDX 2.3 SBOM 与 SLSA v1 provenance payload，记录 artifact SHA-256、source revision、builder identity 和 build parameters。release workflow 已配置为通过 GitHub OIDC/Sigstore 对 package provenance 与 SBOM association 签名。只有获授权的 release job 实际取得 OIDC identity 并发布可验证 attestation 时，签名才构成证据；本地运行不能满足或模拟该条件。Release candidate 同时必须通过 public API、dependency license、vulnerability、deterministic package、clean native-RID consumer 与完整 desktop acceptance gates。
