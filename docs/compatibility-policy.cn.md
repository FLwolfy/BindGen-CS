# 当前契约与发布证据

[English](compatibility-policy.md) | [文档索引](README.cn.md)

BindGen-CS 维护一套当前配置、lowering 和生成契约。修改源码时同步调用方、示例、schema、API snapshot 与测试；删除的 API 和旧配置布局不保留为 fallback 路径。

## 配置与公开 API

- 配置使用当前属性名，不包含配置 schema revision、迁移 reader 或旧 emitter。
- C# 输出只消费 Binding IR；C++ 桥输出只消费 Bridge IR。分析层管理 Clang AST，emitter 接收冻结的生成事实。
- 文件组合保留显式 JSON 值，包括与默认值相同的值。每个文件的相对引用由该文件所在目录解析，组合结果移除继承引用。
- API snapshot 用于审阅变化。更新 snapshot 时必须检查实际消费者和文档，snapshot 本身不能证明功能正确。
- 插件契约 revision handshake 用于拒绝不同扩展 ABI 的程序集，是加载完整性检查，不是配置迁移机制。

## ABI、所有权与平台支持

分配器、回调、异步或 ownership 语义缺失时不能猜测 managed 便捷 API。C 生成链在能安全表达时保留原始 ABI，并报告缺少的语义；`strictSafetySeverity` 决定这些诊断是否阻止发布。无法表达的 C++ lowering 必须失败，除非显式注册的扩展定义了转换。

目标描述记录解析所用 ABI 和 SDK 输入。解析或交叉编译成功不能证明运行时支持。平台证据必须包含同一源码 revision 下、独立 BGCS 消费者实际调用原生 API 的结果，并记录 import mode、managed runtime、工具链和宿主。引擎联调报告属于该引擎的证据，不能替代 BGCS 独立验收。

生成和打包先准备完整候选，失败保留旧输出。有关联的 native 和 managed 输出目录由共同 publication owner 管理；读取方也需要遵守该所有权，因为文件系统不能同时重命名多个目录。

## 发布证据

`bindgen-cs supply-chain` 输出确定性的 SPDX 2.3 SBOM 与 SLSA v1 provenance，记录产物 SHA-256、源码 revision、builder 身份与构建参数。发布自动化可以生成 GitHub OIDC/Sigstore attestations；只有实际取得身份并发布可验证 attestation 的授权 job 才能证明签名发布，本地执行不能替代。

Package 验收还需要检查公开 API、依赖许可证、漏洞、确定性包内容、干净消费者及支持目标的实际调用矩阵。未执行的宿主和架构在验收报告中明确标为未验证。
