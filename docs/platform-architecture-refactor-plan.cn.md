# BGCS 架构重构执行计划

## 实施中的职责细化

原计划列出的 `Preprocessing/Preprocessor.cs` 被实际的
`CSharp/Preprocessing/CSharpPreprocessor.cs` 替代：原实现没有执行预处理，
新的具体 C# 前端处理条件编译并保留源位置。C/C++ 预处理属于 Clang。
这是语言职责的收口，其他目标、IR 与 Runtime 不承担 C# directive 语义。

### 1. 仓库及八个生产项目

```text
C:\Dev\GameEngineDev\BindGen-CS\
├─ BindGen-CS.sln
├─ CONTRIBUTING.md                                 独立开发规范入口
├─ .editorconfig                                   [新增]
├─ Directory.Build.props                           [新增] 共同编译及 XML 约束
├─ global.json                                     生成器工程 SDK 选择
├─ README.md                                       专业、入门友好的英文入口
├─ README.cn.md                                    中文入口
├─ src/
│  ├─ BGCS.Core/
│  ├─ BGCS.CppAst/
│  ├─ BGCS.Intermediate/
│  ├─ BGCS/
│  ├─ BGCS.Cpp2C/
│  ├─ BGCS.Language/
│  ├─ BGCS.Runtime/
│  └─ BGCS.Tool/
├─ tests/
├─ examples/
├─ scripts/
├─ docs/
├─ extern/                                         解析器资源及第三方内容
├─ .github/workflows/
└─ artifacts/                                      [产物] 构建和独立验收
```

依赖：

```text
BGCS.Tool → BGCS + BGCS.Cpp2C
BGCS → BGCS.Core + BGCS.CppAst + BGCS.Intermediate + BGCS.Language
BGCS.Cpp2C → BGCS.Core + BGCS.CppAst + BGCS.Intermediate
BGCS.CppAst → BGCS.Core
BGCS.Language → BGCS.Core

BGCS.Intermediate：不依赖其他 BGCS 项目
BGCS.Runtime：独立互操作库
```

### 2. Core：中立契约与基础能力

```text
src/BGCS.Core/
├─ BGCS.Core.csproj
├─ Targeting/                                      [新增]
│  ├─ NativeTargetId.cs
│  ├─ NativeTargetRequest.cs
│  ├─ NativeTargetDescriptor.cs
│  ├─ NativeToolchainDescriptor.cs
│  └─ INativeTargetProvider.cs
├─ Extensibility/
│  ├─ IBindingPlugin.cs                            拆分现有巨型契约文件
│  ├─ IBindingPluginHost.cs
│  ├─ ICacheFingerprintProvider.cs
│  ├─ BindingPluginService.cs
│  ├─ BindingPluginRegistry.cs
│  └─ BindingPluginLoader.cs
├─ Collections/
│  └─ 现有通用集合
├─ Logging/
│  ├─ LogSeverity.cs
│  └─ LogMessage.cs
├─ Caching/
│  ├─ IncrementalGenerationCache.cs
│  └─ GenCacheFile.cs
├─ IO/
│  └─ OutputDirectoryTransaction.cs
├─ Writing/                                        [重构] 通用写出机制
│  ├─ ICodeWriter.cs
│  └─ CodeWriter.cs
└─ Text/                                           [重构] 通用文本与标识符机制
   └─ 通用文本实现
```

Core 删除 CppAst 引用。AST 参数、C++ 类型和语言映射迁入对应前端或生成器。

### 3. CppAst：解析器及 Clang 边界

```text
src/BGCS.CppAst/
├─ BGCS.CppAst.csproj
├─ Parsing/
│  ├─ CppParser.cs
│  ├─ CppParserOptions.cs
│  ├─ CppParserKind.cs
│  ├─ CppModelBuilder.cs
│  ├─ CppModelBuilder.Types.cs
│  ├─ CppModelBuilder.Expressions.cs
│  ├─ CppModelBuilder.Attributes.cs
│  ├─ CppModelBuilder.VisitMember.cs
│  └─ Visitors/                                    现有 visitor 按职责归并
├─ Model/
│  ├─ CppElement.cs
│  ├─ Declarations/
│  ├─ Types/
│  ├─ Expressions/
│  └─ Attributes/
├─ Diagnostics/
│  ├─ CppDiagnosticBag.cs
│  └─ CppDiagnosticMessage.cs
├─ Targeting/
│  ├─ ClangTargetResolver.cs                        [新增] 中立目标 → Clang 参数
│  ├─ CppToolchainDiscovery.cs                      [重构] 显式宿主及 SDK 探测
│  └─ Providers/
│     ├─ WindowsNativeTargetProvider.cs            [新增]
│     ├─ UnixNativeTargetProvider.cs               [新增]
│     ├─ AppleNativeTargetProvider.cs              [新增]
│     └─ EmscriptenNativeTargetProvider.cs          [新增]
├─ Interop/
│  ├─ ClangNativeRuntime.cs
│  └─ ClangResourceHeaders.cs
├─ Collections/
├─ AttributeParsing/                               规范化现有命名
└─ Utilities/                                      限于 Parser 的实现
```

目标 provider 解析目标描述；Clang resolver 转换 triple、sysroot、include、defines 和 ABI 参数。

bundled builtin headers 与所用 libclang 匹配。宿主 SDK headers 与 builtin headers分别管理。当前 iOS 目标描述在 Apple provider 中区分设备与模拟器。

### 4. Intermediate：真正冻结的生成模型

```text
src/BGCS.Intermediate/
├─ BGCS.Intermediate.csproj
├─ BindingModule.cs                                [迁入、重构]
├─ BindingType.cs
├─ BindingFunction.cs
├─ BindingDelegate.cs
├─ BindingConstant.cs
├─ BindingImport.cs
├─ MarshallingPlan.cs
├─ BindingGenerationResult.cs
├─ Diagnostics/
│  ├─ BindingDiagnostic.cs
│  └─ BindingDiagnosticCatalog.cs
├─ Emission/
│  ├─ IBindingEmitter.cs                           [迁入]
│  └─ EmissionContext.cs                           拆分独立类型
└─ Bridges/                                        [新增]
   ├─ CppBridgeModule.cs
   ├─ CppBridgeType.cs
   ├─ CppBridgeFunction.cs
   ├─ CppBridgeOperation.cs
   └─ ICppBridgeEmitter.cs
```

- 源码实际属于本项目，删除 `Compile Link`。
- builder 在分析层，结果模型只读。
- IR 保存布局、符号、ABI、ownership 和 marshalling 事实。
- 语义模型不携带 Clang 对象、运行服务、SDK 探测或 emitter 回调。
- 独立 emission request 承载输出路径等写出选项。

### 5. BGCS：C → C# 生成链

```text
src/BGCS/
├─ BGCS.csproj
├─ Facade/
│  └─ BindingGenerator.cs                          主要公开入口
├─ Application/
│  └─ BindingGenerationPipeline.cs                 完整生成编排
├─ Configuration/
│  ├─ CsCodeGeneratorConfig.cs                      归并当前配置职责
│  ├─ IGeneratorConfig.cs                          [迁入、收窄]
│  ├─ ConfigLoader.cs
│  ├─ ConfigDocumentLoader.cs
│  ├─ ConfigValidator.cs
│  ├─ PresetResolver.cs
│  ├─ MarshallingMapping.cs
│  └─ 映射与命名配置
├─ Analysis/
│  ├─ DeclarationGraph.cs
│  ├─ BindingModuleAnalyzer.cs
│  ├─ TypeAnalyzer.cs
│  ├─ AbiLayoutAnalyzer.cs
│  ├─ OwnershipAnalyzer.cs
│  ├─ StrictSafetyAnalyzer.cs
│  └─ OverloadPlanner.cs
├─ Conversion/
│  ├─ CppTypeConverter.cs
│  └─ PlatformAbiTypeClassifier.cs
├─ Emission/
│  ├─ CSharpEmitter.cs
│  ├─ RuntimeEmitter.cs
│  └─ SingleFileComposer.cs
├─ Patching/
│  ├─ IPatch.cs
│  ├─ IPrePatch.cs
│  ├─ IPostPatch.cs
│  ├─ PatchEngine.cs
│  ├─ PatchContext.cs
│  └─ 具体 Patch
├─ Output/
│  ├─ GeneratedOutputTransaction.cs
│  └─ SingleFileOutputNameResolver.cs
└─ Metadata/
   └─ 当前生成元数据
```

收口旧 generator、builder 与 facade 中重复的编排职责。保留必要公开能力，通过唯一应用流程执行。

### 6. Cpp2C：C++ 桥生成及原生构建

```text
src/BGCS.Cpp2C/
├─ BGCS.Cpp2C.csproj
├─ Application/
│  └─ CppBridgeGenerationPipeline.cs                [新增] 唯一桥生成编排
├─ Configuration/
│  ├─ Cpp2CGeneratorConfig.cs                       归并当前配置职责
│  └─ Cpp2CConfigValidator.cs
├─ Analysis/
│  └─ CppBridgeModuleAnalyzer.cs                    [重构] 构建冻结 Bridge IR
├─ Lowering/
│  ├─ ICppTypeLowering.cs                          拆分既有协议
│  ├─ CppLoweringRegistry.cs
│  ├─ CppLoweringRecipes.cs
│  ├─ BuiltInCppTypeLowerings.cs
│  └─ CppExtensionArtifactEmitter.cs
├─ Emission/
│  └─ CBridgeEmitter.cs                             [重构] 只消费 Bridge IR
├─ Build/
│  ├─ NativeBuildPlan.cs
│  ├─ NativeBuildExecutor.cs
│  ├─ NativeBuildPaths.cs
│  ├─ NativeExportInspector.cs
│  ├─ NativeBinaryIdentity.cs
│  ├─ CppBridgeBuildManifest.cs
│  ├─ CppBridgeBuildManifestEmitter.cs
│  ├─ CppBridgeBuildManifestSerializer.cs
│  └─ Providers/
│     ├─ ClangNativeBuildProvider.cs
│     ├─ ClangClNativeBuildProvider.cs
│     ├─ CMakeNativeBuildProvider.cs
│     ├─ MesonNativeBuildProvider.cs
│     └─ MSBuildNativeBuildProvider.cs
└─ Metadata/
   └─ 桥生成元数据
```

唯一链路：

```text
C++ → Clang AST → 分析与 Lowering → Bridge IR
    → C header / C++ bridge → C header 解析
    → Binding IR → C# emitter
```

Bridge IR 覆盖当前支持的构造、销毁、方法、继承、模板实例化、布局及回调 lowering。删除 `EmitAst` 和 AST 直接输出路径。

### 7. Language、Runtime 与 Tool

```text
src/BGCS.Language/
├─ BGCS.Language.csproj
├─ Lexing/
│  ├─ Lexer.cs
│  ├─ Token.cs
│  └─ TokenType.cs
├─ Parsing/
│  ├─ ParserBase.cs
│  ├─ ParserContext.cs
│  └─ ParserResult.cs
├─ Preprocessing/
│  └─ Preprocessor.cs
├─ CSharp/
│  ├─ CSharpParser.cs
│  ├─ Nodes/
│  └─ Analyzers/
└─ Cpp/
   ├─ CppMacroParser.cs
   └─ Analysers/

src/BGCS.Runtime/
├─ BGCS.Runtime.csproj
├─ Primitives/
│  ├─ Pointer.cs
│  ├─ ConstPointer.cs
│  ├─ Bool8.cs
│  ├─ Bool32.cs
│  ├─ Bitfield.cs
│  ├─ Atomic.cs
│  ├─ NativeLongDouble.cs
│  └─ Aapcs64VaList.cs
├─ Interop/
│  ├─ INativeContext.cs
│  ├─ NativeLibraryContext.cs
│  ├─ NativeLibrary.cs
│  ├─ LibraryLoader.cs
│  ├─ FunctionTable.cs
│  ├─ NativeNameAttribute.cs
│  ├─ NativeCallback.cs
│  ├─ NativeCallbackRegistration.cs
│  ├─ NativeCallbackRegistry.cs
│  ├─ NativeCallbackExceptionBoundary.cs
│  ├─ NativeAotCallback.cs
│  └─ NativeAsyncOperation.cs
└─ Utilities/
   └─ 现有互操作基础实现

src/BGCS.Tool/
├─ BGCS.Tool.csproj
├─ Program.cs                                      唯一生产 CLI
├─ WorkspaceCommand.cs
├─ Commands/
│  ├─ InitCommand.cs
│  ├─ GenerateCommand.cs                           [新增] 提取命令处理
│  ├─ BridgeCommand.cs                             [新增]
│  ├─ NativeBuildCommand.cs
│  ├─ ExplainCommand.cs
│  ├─ SchemaCommand.cs
│  ├─ SupplyChainCommand.cs
│  └─ ValidateCommand.cs                           [新增]
├─ Validation/
│  ├─ CSharpStyleValidator.cs                      [新增]
│  ├─ ArchitectureValidator.cs                     [新增]
│  ├─ ApiSnapshotValidator.cs                      [迁入]
│  └─ DependencyAudit.cs                           [迁入]
└─ Output/
   ├─ GenerationDiagnosticWriter.cs
   └─ GeneratedSourceComparison.cs
```

语言前端、互操作库与 CLI 各自独立。API snapshot 和 dependency audit 的独立工具 Program 并入验证命令。

### 8. BGCS 文档、示例及输出

```text
docs/
├─ README.md
├─ README.cn.md
├─ csharp-development-standard.cn.md                [新增] 独立规范副本
├─ architecture.md                                 [重构]
├─ architecture.cn.md                              [重构]
├─ platform-architecture-refactor-plan.cn.md        [新增] 完整执行清单
├─ architecture-refactor-acceptance.cn.md           [新增] 实际验收
├─ getting-started.md
├─ getting-started.cn.md
├─ configuration-guide.md
├─ configuration-guide.cn.md
├─ capabilities.md
├─ capabilities.cn.md
├─ lowering.md
├─ lowering.cn.md
└─ testing.md

examples/
├─ QuickStart/                                     最小 C API → C# 示例
├─ NativeShim/                                     C++ facade → C 桥 → C# 示例
└─ LoweringPlugin/                                 独立扩展示例

artifacts/
├─ generation/<targetId>/<fingerprint>/
│  ├─ Native/                                      C 桥
│  ├─ Generated/                                   managed 绑定
│  └─ diagnostics/
├─ native/<targetId>/<fingerprint>/                 编译及链接产物
├─ packages/                                       本地 package 验证
└─ acceptance/<runId>/
   ├─ environment.json
   ├─ report.json
   └─ logs/
```

BGCS 使用自身 fixture 和消费者验证能力，报告与下游消费者的联调报告独立。


## 执行状态

基线：2026-10-04，`3fc481d`，开始时工作区干净。

- [x] 独立 C# 规范及验证入口。
- [x] Core 与 Parser 解耦；IR 源码归属。
- [x] 开放原生目标、统一目标解析。
- [x] Cpp2C 完整冻结 IR；删除 AST 输出。
- [x] 全量命名、排版、公开文档及消费者。
- [x] 11 个托管测试项目、独立 C/C++、Wasm、NativeAOT、负向验收。

上述勾选对应当前 Windows 独立验收，完整命令、实际调用、负向结果和其他主机边界见
[重构验收](architecture-refactor-acceptance.cn.md)。远程 CI、其他主机和外部项目联调不由本机结果代替。

生成链：C++ → Clang AST → 分析/Lowering → 冻结 Bridge IR → C 桥 → C header 解析 → Binding IR → C#。

BGCS 独立运行并使用自身 fixture；SDK、目标 ABI、ownership、回调和释放构成验收边界。生成器宿主与目标平台分开。生成输出经 staging 验证后提交，取消或失败保留旧完整输出。

全量规范整理分离纯排版、命名与行为变化；公共契约变化同步 tests、API snapshot、文档及当前消费者。实际证据记录到架构重构验收报告。
