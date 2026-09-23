# 架构说明

[Wiki](README.cn.md) | [English](architecture.md) | [能力矩阵](capabilities.cn.md)

主 C# 生成只使用共享 Binding IR。预发布 compatibility emitter 与旧配置迁移已经删除；架构结论以真实数据流为准。

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
          ├─ CSharpEmitter(BindingModule)
          ├─ post-patch / SingleFile / optional Runtime
          └─ GeneratedOutputTransaction.commit
```

关键事实：

- `BindingModule` 是真实分析结果，用于 structured result、安全诊断和新 emitter API。
- `CSharpEmitter` 只消费 IR，并且是唯一 configured C# 输出路径。
- IR-native 路径承载 constant、alias、delegate、opaque handle、enum underlying type、匿名/嵌套 record、bitfield、全部 import mode，以及 public raw/string/span/ref/out friendly overload。
- IR-native C# emitter 会在写文件前验证能否无损表达全部语义；配置生成对暂不支持的语义返回结构化 `BGCSCS001`，绝不隐式回退，失败时保留 last-good output。缺少字段定义的 opaque storage 可以通过 pointer 使用，但按值调用会被拒绝，因为仅凭 size/alignment 无法证明 target ABI classification。
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

C# 目标态已经实现：emitter 不遍历可变 Clang AST。C++ Bridge 对尚未进入共享 IR 的结构保留显式 AST lowering 边界。

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

`CsCodeGenerator` 是可嵌入 generator API；`BGCS.Facade.BindingGenerator` 返回 `BindingGenerationResult`。Facade 负责参数和用例入口，不应增加 AST 遍历或 output composition。

### Configuration

- `ConfigLoader`：配置读取与相对路径上下文。
- `ConfigComposer`：BaseConfig merge 和循环检测。
- `ConfigValidator`：写输出前验证 target、path、mapping 和 output invariants。
- `PresetResolver`：通用 target/API/output 默认值，不包含库特定硬编码。

### Parsing

`BGCS.CppAst` 使用 Clang 构建 declaration/type/comment/token model，并通过 `CppTarget` 与 `CppToolchainDiscovery` 注入 target triple、system includes 和 sysroot。

### Target 与 Runtime 可移植性

- `BGCS.CppAst` 会按 RID 选择并预加载 Windows、macOS、Linux x64/arm64 的 Clang/ClangSharp runtime，不依赖宿主全局 `libclang` 名称。
- ABI classifier 集中处理 target-dependent primitive 和 compiler carrier。Linux Arm64 的 unsigned plain `char` 与 AAPCS64 `va_list` 已显式建模；SysV x64 的 array-decayed `va_list` 保持独立规则。
- `BGCS.Runtime.NativeLibrary` 使用 .NET 跨平台 loader 处理 module 与 export，避免 `libdl.so` 等平台 soname 假设。
- Workspace target 子目录与 target-specific snapshot/report 防止一个宿主生成的 ABI 被另一个宿主误编译或误验收。

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

它既是可用的分析结果，也是 C# emission 的唯一输入。C++ Bridge 保留独立且明确的 AST-specific lowering 边界。

### Emission

- `CSharpEmitter`：唯一 configured C# emitter，包含 IR-native `Emit`、friendly lowering 与 lossless capability validation。
- `RuntimeEmitter`：从 module 生成 standalone runtime contract。
- `SingleFileComposer`：通过 Roslyn syntax tree 做确定性合并。
- `CBridgeEmitter`：生成 C++ → C wrapper；当前仍需要 AST-specific generation data。

### Native build

- `CppBridgeBuildManifest`：版本化、确定性、相对配置目录的 source、target、toolchain 与 link input 描述。
- `INativeBuildPipelineProvider`：不通过 shell quoting，把 manifest 转换为确定性的 build input 与 argument-list 多步骤计划。
- Provider：direct Clang/GNU、clang-cl、CMake、Meson 与 MSBuild。
- `NativeBuildExecutor`：带 timeout、stdout/stderr 捕获的多步骤进程执行器，不修改全局 current directory。
- `NativeExportInspector`：通过 `nm` 或 `dumpbin` 对比生成的公开 C symbol 与实际 artifact export table。

配置驱动的 C++ 生成也从显式 configuration directory 解析 header、include、sysroot、compiler path、output 和文件型 `BaseConfig` 链；它不会修改 `Environment.CurrentDirectory`，因此并发 generator 不会争用进程级路径状态。

## Cache 与 Plugin

C 与 C++ 配置生成使用 immutable SHA-256 output cache。Key 包含 generator identity、序列化配置、parser arguments、解析后的 compiler identity/version、plugin/adapter fingerprint，以及发现到的全部 C/C++ 输入精确内容。Entry 原子发布，并通过同一个 output transaction 恢复；无法稳定 fingerprint 的自定义状态会关闭 cache hit。

`BindingPluginContract` version 1 提供显式 assembly entry point 和确定性 typed registration。Plugin assembly 使用隔离 dependency resolver，同时共享 host contract；整份 assembly 会先完整校验再原子注册，assembly 内容 hash 与 plugin version 会进入 cache key。C++ plugin 可注册 `ICppTypeAdapter` / `ICppCallableAdapter`，C# plugin 可注册附加 `IBindingEmitter`。

### Output

`GeneratedOutputTransaction` / `OutputDirectoryTransaction` 在 staging 目录生成。只有 generation 与 patch 全部成功后才替换正式目录，因此失败保留 last-good bindings。

## 完成条件

C# 路径完成条件是：`BindingGenerationPipeline` 调用 `CSharpEmitter.Emit(BindingModule, ...)`；metadata/patch 行为要么是 IR transform，要么是明确 source post-processing；新增功能的 source/API/compile tests 与完整真实库矩阵全部通过。configured output 中不存在 compatibility emitter。

C++ Bridge 的对应完成条件是：C Bridge emitter 只消费完整 IR，AST 只存在于 analysis 阶段。

## 扩展模型

新扩展优先接收 immutable config/request、Binding IR 和 scoped diagnostics。不要依赖 CLI、修改全局 current directory，或把单一 native library 的名称/布局硬编码进 core。库特定事实应留在消费项目配置、preset composition 或显式 custom adapter 中。
