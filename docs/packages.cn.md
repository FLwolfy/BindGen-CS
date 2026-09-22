# NuGet 包与公开 API

[English](packages.md) | [Wiki 首页](README.cn.md) | [API 参考](api.md)

## 应该安装哪个包？

| 目标 | 安装 | 说明 |
| --- | --- | --- |
| 使用命令行 | `dotnet tool install --global BindGen-CS` | 提供 `bindgen-cs`；不要把 Tool 包加入应用项目。 |
| 在程序中嵌入 C/C++ → C# 生成 | `BGCS` | 主 facade、配置、分析、C# emission、patch 和兼容 generation passes。 |
| 为 C++ 生成 C ABI bridge | `BGCS.Cpp2C` | 嵌入 bridge 生成时安装；CLI Tool 已携带该包。 |
| 编译并运行生成的 bindings | `BGCS.Runtime` | 通常安装到消费生成代码的应用；也可选择 standalone Runtime source。 |
| 开发 emitter 或 IR 工具 | `BGCS.Intermediate` | 零依赖共享 contract，不需要加载 Clang、Roslyn 或 Runtime。 |
| 开发 parser/tooling 扩展 | `BGCS.CppAst`、`BGCS.Core`、`BGCS.Language` | 高级实现包，通常由入口包传递恢复。 |

全部发布包统一版本，并在发布前通过干净 NuGet consumer 验证。

## `BindGen-CS` Tool

导出命令：

```text
init, doctor, validate, inspect, generate, build, diff, schema, bridge, version
```

Tool 在隔离安装环境中依赖 `BGCS` 和 `BGCS.Cpp2C`。最终生成代码不依赖 Tool 包。

## `BGCS`

主要公开 API：

- `CsCodeGenerator`、`CsCodeGeneratorConfig`、`GeneratorBuilder`、`BatchGenerator`；
- `BGCS.Facade.BindingGenerator`；
- `BindingGenerationPipeline`；
- `ConfigLoader`、`ConfigValidator`、`PresetResolver`；
- `DeclarationGraph`、`BindingModuleAnalyzer`、`TypeAnalyzer`、`AbiLayoutAnalyzer`、`OwnershipAnalyzer`、`OverloadPlanner`；
- `CSharpEmitter`、`RuntimeEmitter`、`SingleFileComposer`；
- generation/preprocess steps、function rules 和 parameter writers；
- patching 与 generator metadata API；
- 用于显式 C 可变参数签名的 `VariadicFunctionVariant`。

直接依赖包括 `BGCS.Intermediate`、`BGCS.Core`、`BGCS.Language`、`BGCS.CppAst` 和 Roslyn。已经移除历史 `CommandLineParser` 依赖。

## `BGCS.Cpp2C`

主要公开 API：

- `Cpp2CCodeGenerator`、`Cpp2CGeneratorConfig`；
- `BGCS.Cpp2C.Emission.CBridgeEmitter`；
- bridge generation-step 扩展点；
- C/C++ type lowering helpers 和生成函数 metadata。

当前 bridge 支持 class、构造/析构、instance/static method、namespace free function、异常通道、显式 class-template specialization 和带 pointer adjustment 的 inheritance cast。

## `BGCS.Intermediate`

该包不依赖任何其他 BGCS assembly，导出：

- `BindingModule`；
- `BindingType`、`BindingField`、`BindingEnumMember`、`BindingTypeReference`；
- `BindingFunction`、`BindingParameter`；
- `MarshallingPlan`；
- `BindingGenerationResult`、`BindingDiagnostic`；
- `IBindingEmitter`、`EmissionContext`；
- type/function/direction/ownership/encoding/marshalling enum。

适合不应加载 Clang 或 Roslyn 的 analyzer、API diff、其他语言 emitter 和 build integration。

## `BGCS.Runtime`

主要公开运行时 API：

- `Bool8`、`Bool32`；
- `Pointer<T>`、`ConstPointer<T>`；
- `Atomic<T>`；
- `NativeCallback<T>`；
- `NativeCallbackRegistry<TKey,TDelegate>`；
- `INativeContext`、`NativeLibraryContext`；
- `NativeLibrary`、`LibraryLoader`、`TargetPlatform`、`ResolvePathHandler`；
- `NativeNameAttribute`、`SourceLocationAttribute`、`NativeNameType`；
- `Utils` allocation、UTF-8/UTF-16、pointer 和 array helpers。

生成代码通常引用本包。`GenerateRuntimeSource=true` 会改为输出带 guard 的 standalone `Runtime.cs`；同时使用 package 和嵌入 Runtime 时应定义 `BGCS_RUNTIME_EXTERNAL`。

## 高级实现包

- `BGCS.CppAst`：基于 ClangSharp 的 C/C++ AST model、parser options、visitor、declaration、type、expression、comment、token 和 diagnostics。
- `BGCS.Core`：output transaction、code writer、mapping、metadata、logging、collection 和 path/file utilities。
- `BGCS.Language`：lexer、parser、preprocessor expression、syntax node、diagnostics 和 analyzer。

除非直接使用这些扩展 API，普通应用不应单独安装它们。

## 当前弊端

- 已验证 `std::string`、输入 `std::span<T>`、blittable `std::optional<T>`、ownership-transfer `std::unique_ptr<T>` 和配置式 pure-virtual callback proxy。string、vector、span input/return、optional、unique_ptr、shared_ptr 默认 adapter 已验证；`std::variant` 和 non-blittable optional alternative 仍需显式 custom lowering。
- 仅凭 pointer 语法无法可靠推断 ownership 和 allocator 语义。
- typed C variadic 当前要求 `DllImport` 和显式完成参数提升后的类型。
- 五个真实 C 库 gate 与 bimg C++ Bridge 会编译生成输出并检查 target-specific 确定性 API snapshot；InnoEngine 还会单独构建其 native binary，并执行全部六个 native binding 测试项目。
- 完整 solution、生成消费者和 package smoke project 均以 warning-as-error 模式通过编译。
