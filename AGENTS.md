# BGCS 开发规范

手写 C# 必须遵守 [通用 C# 开发规范](docs/csharp-development-standard.cn.md) 和 [架构执行计划](docs/platform-architecture-refactor-plan.cn.md)。

- BGCS 独立于消费方引擎。测试、示例和平台能力证据属于本仓库。
- 生产项目保持八个；BGCS.Core 不依赖 CppAst；IR 源码属于 BGCS.Intermediate；BGCS.Runtime 独立。
- 生成器宿主与原生目标分开。目标通过公开 provider 契约解析；SDK、sysroot、ABI 与 builtin headers 分别管理。
- Cpp2C 与 C# emitter 消费冻结 IR，分析层承担 AST 及 lowering。
- 私有字段 m_；属性及参数 camelCase；方法和类型 PascalCase。多参数声明逐参数换行，右括号与声明起始行对齐，紧随函数体写作 `) {`。
- 所有 using 显式；public/protected API 完整英文 XML。同步当前调用方、配置、文档、API snapshot 和测试。
- 纯排版与语义变更分开验证；不手改生成物或 extern。
- 测试使用公开契约，禁止测试后门、InternalsVisibleTo 或反射穿透。
- 实际原生、Wasm、NativeAOT 调用与负向验收构成平台支持证据；未验收状态明确记录。
- Solution 保留实际使用的项目与分组，避免空分组和无必要的单项目包装层。MSBuild 文件使用两空格缩进、职责空行、逐属性长声明及 `/` ProjectReference 路径；纯整理必须保持条件、metadata、Import、Target 和有效求值顺序。
