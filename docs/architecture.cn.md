# 架构说明

[Wiki](README.cn.md) | [English](architecture.md) | [能力矩阵](capabilities.cn.md)

本文同时描述当前实现和目标架构。二者没有完全重合：共享 Binding IR、analysis 和 emitter contract 已存在，但主 C# 生成仍通过兼容 `GenerationStep` 路径输出。架构质量不能用目录名代替数据流事实。

## 当前真实数据流

```text
CLI / CsCodeGenerator / BindingGenerator
                 ↓
      Config load + composition + validation
                 ↓
       C/C++ parse → CppCompilation
                 ↓
        BindingGenerationPipeline
          ├─ preprocess / pre-patch
          ├─ DeclarationGraph
          ├─ BindingModuleAnalyzer → BindingModule
          ├─ StrictSafetyAnalyzer
          ├─ AstGenerationStepEmitter（兼容 surface）
          │    └─ GenerationStep implementations
          ├─ post-patch / SingleFile / optional Runtime
          └─ GeneratedOutputTransaction.commit
```

关键事实：

- `BindingModule` 是真实分析结果，用于 structured result、安全诊断和新 emitter API。
- `CSharpEmitter` 已经只消费 IR。主 C# output 仍调用独立命名的 `AstGenerationStepEmitter`；这消除了 IR emitter 对 AST 的依赖，但不等于默认路径迁移完成。
- `CSharpEmitter.Emit(BindingModule, EmissionContext)` 是 IR-native 路径，但还不是配置驱动生成的默认实现。
- IR-native C# emitter 会在写文件前验证能否无损表达全部语义；暂不支持的语义通过结构化 `BGCSCS001` 报错，不会静默丢弃。
- C++ Bridge 有独立 analyzer 和 `CBridgeEmitter.EmitAst` 路径，最终也返回 `BindingModule`；它与 C# 共享 contract，但并非所有 emission 都只消费 IR。
- C++ Bridge 可生成带版本的 build manifest；`INativeBuildPipelineProvider` 将其转换为不依赖 shell 的多步骤计划。内置 direct Clang/GNU、clang-cl、CMake、Meson、MSBuild provider，并通过 `nm` / `dumpbin` 检查实际 export。
- CLI `build` 在 pipeline 成功后另建临时 .NET 项目做 warning-as-error 编译；编译验证不属于 `BindingGenerationPipeline` 自身。

## 目标数据流

```text
Configuration → Parsing → Analysis → immutable BindingModule
                                         ↓
                  C# / Runtime / C Bridge / custom emitters
                                         ↓
                            Transactional Output
```

目标态中 emitter 不再遍历可变 Clang AST，也不依赖 legacy generator metadata。当前实现已经具备 contract 和一部分 emitter，但迁移尚未全部完成。

## 依赖规则

依赖应当向内：

```text
BGCS.Tool
  → BGCS facade/application
  → configuration + analysis + emission
  → BGCS.Intermediate
  → BGCS.Core + BGCS.CppAst + BGCS.Language

BGCS.Runtime 独立于生成器程序集
BGCS.Intermediate 不依赖其他 BGCS assembly
```

`Analysis` 和 `Intermediate` 不应引用 CLI。IR contract 不应包含 Roslyn syntax、生成源码字符串、文件系统路径或可变 Clang cursor。

## 分层职责

### Facade

`CsCodeGenerator` 保留兼容 API；`BGCS.Facade.BindingGenerator` 返回 `BindingGenerationResult`。Facade 负责参数和用例入口，不应继续增加 AST 遍历或 output composition。

### Configuration

- `ConfigLoader`：配置读取与相对路径上下文。
- `ConfigComposer`：BaseConfig merge 和循环检测。
- `ConfigValidator`：写输出前验证 target、path、mapping 和 output invariants。
- `PresetResolver`：通用 target/API/output 默认值，不包含库特定硬编码。

### Parsing

`BGCS.CppAst` 使用 Clang 构建 declaration/type/comment/token model，并通过 `CppTarget` 与 `CppToolchainDiscovery` 注入 target triple、system includes 和 sysroot。

### Analysis

- `DeclarationGraph`：声明及其 ABI 依赖排序。
- `TypeAnalyzer`：native type → `BindingTypeReference`。
- `AbiLayoutAnalyzer`：size、alignment、field、array、union 和 bitfield facts。
- `OwnershipAnalyzer`：保守 marshalling/ownership 默认值与显式 mapping merge。
- `OverloadPlanner`：pointer/count、capacity 和 written-count 关系。
- `StrictSafetyAnalyzer`：无法证明的 ownership、allocator、length 和 callback lifetime。

Analyzer 不创建正式输出文件。

### Intermediate

`BGCS.Intermediate` 是零依赖 contract 包，包含 `BindingModule`、type/function/field/parameter、`MarshallingPlan`、diagnostics、`IBindingEmitter` 和 `EmissionContext`。

它目前既是可用的分析结果，也是 legacy emission 迁移的目标模型；不要把“IR 已存在”等同于“所有 emitter 已完全 IR-native”。

### Emission

- `CSharpEmitter`：只包含 IR-native `Emit` 与 lossless capability validation；剩余兼容路径由 `AstGenerationStepEmitter` 隔离。
- `RuntimeEmitter`：从 module 生成 standalone runtime contract。
- `SingleFileComposer`：通过 Roslyn syntax tree 做确定性合并。
- `CBridgeEmitter`：生成 C++ → C wrapper；当前仍需要 AST-specific generation data。

### Native build

- `CppBridgeBuildManifest`：版本化、确定性、相对配置目录的 source、target、toolchain 与 link input 描述。
- `INativeBuildProvider`：不通过 shell quoting，把 manifest 转换为 argument-list process plan。
- `ClangNativeBuildProvider`：内置的跨平台 Clang/GNU-driver 实现。
- `NativeBuildExecutor`：带 timeout、stdout/stderr 捕获的进程执行器，不修改全局 current directory。

配置驱动的 C++ 生成也从显式 configuration directory 解析 header、include、sysroot、compiler path、output 和文件型 `BaseConfig` 链；它不会修改 `Environment.CurrentDirectory`，因此并发 generator 不会争用进程级路径状态。

## Cache 与 Plugin

C 与 C++ 配置生成使用 immutable SHA-256 output cache。Key 包含 generator identity、序列化配置、parser arguments、解析后的 compiler identity/version、plugin/adapter fingerprint，以及发现到的全部 C/C++ 输入精确内容。Entry 原子发布，并通过同一个 output transaction 恢复；无法稳定 fingerprint 的自定义状态会关闭 cache hit。

`BindingPluginContract` version 1 提供显式 assembly entry point 和确定性 typed registration。Plugin assembly 使用隔离 dependency resolver，同时共享 host contract；整份 assembly 会先完整校验再原子注册，assembly 内容 hash 与 plugin version 会进入 cache key。C++ plugin 可注册 `ICppTypeAdapter` / `ICppCallableAdapter`，C# plugin 可注册附加 `IBindingEmitter`。

### Output

`GeneratedOutputTransaction` / `OutputDirectoryTransaction` 在 staging 目录生成。只有 generation 与 patch 全部成功后才替换正式目录，因此失败保留 last-good bindings。

## 迁移完成条件

主 C# 路径只有满足以下条件，才能被称为完全 IR-native：

1. `BindingGenerationPipeline` 调用 `CSharpEmitter.Emit(BindingModule, ...)` 作为默认路径；
2. legacy `GenerationStep` 不再决定公开输出语义；
3. metadata/patch 能力要么转成 IR transform，要么明确限定为 source post-processing；
4. real-library source/public-API snapshots 与 native tests 保持一致；
5. 删除 `AstGenerationStepEmitter` 不会改变生成结果。

C++ Bridge 的对应完成条件是：C Bridge emitter 只消费完整 IR，AST 只存在于 analysis 阶段。

## 扩展模型

新扩展优先接收 immutable config/request、Binding IR 和 scoped diagnostics。不要依赖 CLI、修改全局 current directory，或把单一 native library 的名称/布局硬编码进 core。库特定事实应留在消费项目配置、preset composition 或显式 custom adapter 中。
