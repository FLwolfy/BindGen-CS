# 架构说明

## 源码与命名空间归属

`CppToolchainDiscovery.DiscoverSystemIncludeFolders` 接收选定的 `sysRoot`，
把它传给 compiler driver 并纳入缓存身份。宿主 C++ 解析遵循该 driver 的头文件搜索顺序；
已有显式 C++ 搜索根时关闭隐式搜索，避免不同标准库与 SDK 混用。
增量输入扫描按符号链接解析后的目录去重：SDK 的有效别名仍参与扫描，祖先循环不会无限展开。

配置属于 `BGCS.Configuration` 与 `BGCS.Cpp2C.Configuration`，公开生成入口属于各自的 `Facade`。
可变声明与 overload 分析属于 `Analysis`，目标和语言转换属于 `Conversion`，源码写出属于 `Emission`。
原生构建计划与执行协议属于 `BGCS.Cpp2C.Build`；五个具体 compiler/build-system provider
属于 `BGCS.Cpp2C.Build.Providers`，源码位于 `Build/Providers`。CLI 只在组合入口选择 provider。
`BGCS.Core` 提供通用集合、文本、IO、配置组合和进程执行；冻结模型的实际源码属于
`BGCS.Intermediate`，没有反向链接其他项目源码。

`BGCS.Runtime` 使用程序集级命名空间作为统一消费者入口，`Primitives`、`Interop`、`Utilities`
只组织源码职责。这是项目级命名空间约定，与原生目标和托管部署无关。

指针宽度来自 `CppCompilation` 和分析图。canonical alias 及配置的 variadic carrier 保留目标宽度，
不会使用生成器宿主的指针宽度。C++ template argument pack 由 Parser 展开为类型与整型参数；
bridge lowering 使用其实际布局，不按类型拼写猜测 ABI。分析成功或失败时都会清除配置中的临时 AST
引用；冻结结果不保留 compilation、Clang cursor 或 lowering service。

`BGCS.Core.Execution.ProcessExecutor` 统一负责 Parser 探测、原生构建、导出检查及生成 C# 编译检查的进程生命周期。它并行读取 stdout/stderr、关闭 stdin，并在超时或取消后结束进程树、观察退出、完成输出读取才返回。原生 pipeline 使用单调时钟计算剩余期限。

`bindgen-cs validate architecture <repository-root>` 验证八个生产项目、允许依赖、唯一 CLI、源码归属与中立层禁止 Parser 类型的规则。`bindgen-cs validate style <source-root> [--fix]` 保留调用排版，并在写入前验证语法 token 与 raw string 内容未变化。

Language 的预处理实现是 `BGCS.Language.CSharp.Preprocessing.CSharpPreprocessor`：
Roslyn 处理 C# 条件编译，禁用代码和 directive 替换为空格，保留 offset 与换行。
原来只返回原文的通用 Preprocessor 已删除。C/C++ 预处理继续属于 Clang，
生成器、Runtime 和目标 provider 不承担该语言语义。

[Wiki](README.cn.md) | [English](architecture.md) | [能力矩阵](capabilities.cn.md)

主 C# 生成只使用共享 Binding IR。预发布 compatibility emitter 与旧配置迁移已经删除；架构结论以真实数据流为准。

## 当前真实数据流

BGCS.CppAst 自带 Clang 20 系列内置 resource headers；编译器发现只提供宿主 SDK 与标准库，
排除系统 clang 自己的 resource include。系统 LLVM 升级后不会把新内置语法交给包内旧主版本解析器。
目标 sysroot 与编译器选择仍是显式输入，Web SDK 选择属于调用方工具链；
来源、校验和与第三方许可见 [resource bundle](../extern/clang-resource/README.md)。

编译器的 resource、include 和 fingerprint 查询同时排空 stdout/stderr；超时会终止并观察进程退出，
失败查询仍明确标记为不可用。opaque handle 的 managed API 保持类型化包装，DllImport、LibraryImport
和 FunctionTable 的标量 handle 参数/返回值统一使用指针宽度的 `nint` 原生载体，再经同一类型化 adapter 转换。
这是所有目标和库共用的 ABI 规则，不在生成的调用包装中加入浏览器判断。

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
          └─ OutputDirectoryTransaction.Commit
```

关键事实：

- `BindingModule` 是真实分析结果，用于 structured result、安全诊断和新 emitter API。
- `CSharpEmitter` 只消费 IR，并且是唯一 configured C# 输出路径。
- IR-native 路径承载 constant、alias、delegate、opaque handle、enum underlying type、匿名/嵌套 record、bitfield、全部 import mode，以及 public raw/string/span/ref/out friendly overload。
- IR-native C# emitter 会在写文件前验证能否无损表达全部语义；配置生成对暂不支持的语义返回结构化 `BGCSCS001`，绝不隐式回退，失败时保留 last-good output。缺少字段定义的 opaque storage 可以通过 pointer 使用，但按值调用会被拒绝，因为仅凭 size/alignment 无法证明 target ABI classification。
- C++ Bridge 使用唯一的 `CppBridgeGenerationPipeline`：analyzer 将 AST 降低为冻结的 `CppBridgeModule` facts 和源码操作，`ICppBridgeEmitter` 只消费该结果。原先的直接 AST emitter 已删除。生成的 C header 随后进入同一条 C → Binding IR → C# 链。
- C++ Bridge 生成当前格式的 build manifest；`INativeBuildPipelineProvider` 将其转换为不依赖 shell 的多步骤计划。内置 direct Clang/GNU、clang-cl、CMake、Meson、MSBuild provider，并通过 `nm` / `dumpbin` 检查实际 export。
- CLI `build` 在 pipeline 成功后另建临时 .NET 项目做 warning-as-error 编译；编译验证不属于 `BindingGenerationPipeline` 自身。

## 目标数据流

```text
Configuration → Parsing → Analysis → immutable BindingModule
                                         ↓
                  C# / Runtime / C Bridge / custom emitters
                                         ↓
                            Transactional Output
```

C# 与 C++ bridge 分别使用冻结 Binding IR 和 Bridge IR；AST 与 C++ lowering 只存在于对应分析阶段。

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

`Analysis` 和 `Intermediate` 不引用 CLI。语义 facts 不携带 Roslyn syntax、Clang cursor、服务或回调。Bridge lowering 输出冻结的目标源码单元；实际输出根由 emission request 管理。

消费者应用应在自己的仓库维护 binding 配置、native 源码、自定义 shim、生成产物和集成测试。BGCS 仓库只负责通用生成器/Runtime 和独立固定版本的上游测试语料；BGCS CI 不需要检出任何相邻应用仓库。

## 分层职责

### Facade

`CsCodeGenerator` 是可嵌入 generator API；`BGCS.Facade.BindingGenerator` 返回 `BindingGenerationResult`。Facade 负责参数和用例入口，不应增加 AST 遍历或 output composition。

`BindingGenerationPipeline` 是非公开应用编排，所有入口共用它。
Facade 返回实际完成结果；编排未发布结果时明确失败，不合成成功或空模块结果。
`IncrementalGenerationCache` 对源与缓存条目按稳定顺序取得文件发布锁，校验精确文件集合和 SHA-256 后恢复。
损坏缓存视为 miss；输出与缓存树重叠被拒绝。目录事务使用 `stagingPath`，缓存身份使用 `value/inputFileCount`。

目录安装及回滚共用 Core 的内部 rename 边界。Windows 的短期访问或共享拒绝最多重试两秒；
持续拒绝保留原异常，并恢复尚未完成提交的旧树。提交阶段不因临时读者跳过文件、删除锁 inode，
也不把部分输出视为成功。所有平台仍使用同一组公开目录事务契约。

### Configuration

- `ConfigLoader`：配置读取与相对路径上下文。
- `ConfigComposer`：BaseConfig merge 和循环检测。
- `ConfigValidator`：写输出前验证 target、path、mapping 和 output invariants。
- `PresetResolver`：通用 target/API/output 默认值，不包含库特定硬编码。

### Parsing

`BGCS.CppAst` 使用 Clang 构建 declaration/type/comment/token model；`ClangTargetResolver` 通过中立 `INativeTargetProvider` 契约解析目标，`CppToolchainDiscovery` 提供显式主机工具链探测。

### Target 与 Runtime 可移植性

- `BGCS.CppAst` 优先选择并预加载上游已提供的 RID 专用 Clang/ClangSharp runtime。ClangSharp 20.1.2 没有发布 macOS x64 native 包：Intel CI 通过 `scripts/setup-macos-x64-clang-runtime.sh` 构建同版本 companion 与 LLVM 20 runtime，并设置 `BGCS_CLANG_RUNTIME_DIR`。缺失资产会明确报错，不会暗中降低 parser 版本。
- ABI classifier 集中处理 target-dependent primitive 和 compiler carrier。Linux Arm64 的 unsigned plain `char` 与 AAPCS64 `va_list` 已显式建模；SysV x64 的 array-decayed `va_list` 保持独立规则。
- `BGCS.Runtime.NativeLibrary` 使用 .NET 跨平台 loader 处理 module 与 export，避免 `libdl.so` 等平台 soname 假设。
- Workspace target 子目录与 target-specific snapshot/report 防止一个宿主生成的 ABI 被另一个宿主误编译或误验收。

### Analysis

- `DeclarationGraph`：声明及其 ABI 依赖排序。
- `TypeAnalyzer`：native type → `BindingTypeReference`。
- `AbiLayoutAnalyzer`：size、alignment、field、array、union 和 bitfield facts。

位域使用 Clang 的绝对 bit offset 和声明宽度，边界检查不把声明整数类型的完整大小当作每个位域的存储大小。
IR emitter 为相邻位域生成精确的重叠字节存储；`Bitfield` 的 Span API 读取和更新指定范围，保留邻接位并按声明宽度进行符号扩展。
因此 signed、unsigned 和 enum 可以共享同一原生存储，packed record 也不需要猜测整数分配单元。
当前提供的原生目标均为 little-endian；新增其他字节序目标必须补充相应 ABI lowering 和真实调用验收。
- `OwnershipAnalyzer`：保守 marshalling/ownership 默认值与显式 mapping merge。
- `OverloadPlanner`：pointer/count、capacity 和 written-count 关系。
- `StrictSafetyAnalyzer`：无法证明的 ownership、allocator、length 和 callback lifetime。

Analyzer 不创建正式输出文件。

### Intermediate

`BGCS.Intermediate` 是零依赖 contract 包，包含 `BindingModule`、type/function/field/parameter、`MarshallingPlan`、diagnostics、`IBindingEmitter` 和 `EmissionContext`。

Binding IR 是 C# emission 的唯一输入；`Bridges` 中的冻结 Bridge IR 是 C++ bridge emitter 的唯一输入。所有 IR 源码实际属于本项目，不通过 `Compile Link` 反向共享源码。

### Emission

- `CSharpEmitter`：唯一 configured C# emitter，包含 IR-native `Emit`、friendly lowering 与 lossless capability validation。
- `RuntimeEmitter`：从 module 生成 standalone runtime contract。
- `SingleFileComposer`：通过 Roslyn syntax tree 做确定性合并。
- `CBridgeEmitter`：根据冻结 `CppBridgeModule` 写出 C header / C++ bridge，不读取 AST 或重新执行 lowering。

### Native build

- `CppBridgeBuildManifest`：版本化、确定性、相对配置目录的 source、target、toolchain 与 link input 描述。
- `INativeBuildPipelineProvider`：不通过 shell quoting，把 manifest 转换为确定性的 build input 与 argument-list 多步骤计划。
- Provider：direct Clang/GNU、clang-cl、CMake、Meson 与 MSBuild。
- `NativeBuildExecutor`：带 timeout、stdout/stderr 捕获的多步骤进程执行器，不修改全局 current directory。
- `NativeExportInspector`：通过 `nm` 或 `dumpbin` 对比生成的公开 C symbol 与实际 artifact export table。

配置驱动的 C++ 生成也从显式 configuration directory 解析 header、include、sysroot、compiler path、output、lowering recipe、native shim 和文件型 `baseConfig` 链；它不会修改 `Environment.CurrentDirectory`，因此并发 generator 不会争用进程级路径状态。

## 原生调用载体

生成的 C# 调用接口保留语义类型；真正的 import 或函数指针签名使用明确的 ABI 载体。
不透明对象使用 `nint`，按值传递的枚举使用冻结 IR 中记录的整数基础类型；枚举指针保留指针形式。
转换集中在生成的调用适配方法，三种 import mode 使用同一规则，不按浏览器或桌面分支。
自定义枚举的 IR 宽度跟随其声明的基础类型，不再固定为四字节。

独立调用 fixture 同时传入并返回超过 32 位的整数和枚举值，验证实际调用保留高位。
这能够发现仅检查 `sizeof` 无法发现的运行时调用签名错误。

## Cache 与 Plugin

C 与 C++ 配置生成使用 immutable SHA-256 output cache。Key 包含 generator identity、序列化配置、parser arguments、解析后的 compiler identity/version、plugin/lowering/shim fingerprint，以及发现到的全部 C/C++ 输入精确内容。Entry 原子发布，并通过同一个 output transaction 恢复；无法稳定 fingerprint 的自定义状态会关闭 cache hit。

`BindingPluginContract.C_CURRENT_VERSION` 提供加载时 revision 握手、显式 assembly entry point 和确定性 typed registration；不会加载更早的 plugin contract。C++ plugin 注册 `ICppTypeLowering`、`ICppCallableLowering` 和 `ICppArtifactContributor`；同一 registry 同时承载 built-in、声明式 recipe 与 plugin lowering。Plugin assembly 使用隔离 dependency resolver 并原子注册，assembly 内容 hash、plugin version 和 lowering fingerprint 进入 cache key。

### Output

`GeneratedOutputTransaction` / `OutputDirectoryTransaction` 在 staging 目录生成。只有 generation 与 patch 全部成功后才替换正式目录，因此失败保留 last-good bindings。

## 完成条件

C# 路径完成条件是：`BindingGenerationPipeline` 调用 `CSharpEmitter.Emit(BindingModule, ...)`；metadata/patch 行为要么是 IR transform，要么是明确 source post-processing；新增功能的 source/API/compile tests 与完整真实库矩阵全部通过。configured output 中不存在 compatibility emitter。

C++ Bridge 的对应完成条件是：C Bridge emitter 只消费完整 IR，AST 只存在于 analysis 阶段。

## 扩展模型

新扩展优先接收 immutable config/request、Binding IR 和 scoped diagnostics。不要依赖 CLI、修改全局 current directory，或把单一 native library 的名称/布局硬编码进 core。库特定事实应留在声明式 lowering、typed lowering plugin 或显式 C ABI shim 中。详见[最终 lowering 架构](lowering.cn.md)。

## 使用方独立性与 WebAssembly 目标

BGCS 负责解析、ABI 分析、lowering、binding 生成及自己的测试 fixture 和验收报告。使用方负责 native facade、SDK 选择、应用链接、部署和运行时集成。使用方应用构建成功属于使用方的证据，不能替代 BGCS 的独立目标验收。本文及能力矩阵只采用 BGCS 自有证据。

`emscripten-wasm32-emscripten` 选择 Clang triple `wasm32-unknown-emscripten` 及对应的 C/C++ ABI。它复用桌面目标使用的 target/configuration/IR/emitter 契约，不为每个库建立另一套生成器。BGCS 不负责安装 SDK，也不实现浏览器渲染、输入或应用启动。

作者主机与输出目标是独立的：Emscripten 支持 Windows、macOS、Linux 作者主机，输出 WebAssembly；`wasm32` 表示目标的 32 位指针地址模型。WASI 等其他 WebAssembly 环境是不同目标，不能当作 Emscripten 的别名。参见 [Emscripten 安装说明](https://emscripten.org/docs/getting_started/downloads.html)、[WebAssembly 输出](https://emscripten.org/docs/compiling/WebAssembly.html)及 [Clang 交叉编译](https://clang.llvm.org/docs/CrossCompilation.html)。

BGCS 自有 target 测试覆盖 triple 与 record 布局，emitter 测试覆盖三种 import mode 的 opaque handle。独立 fixture 通过生成的 DllImport、LibraryImport、FunctionTable 调用 BGCS 自有 C API 和 C++ bridge。2026-10-04 Windows x64 NativeAOT 与 Edge/Wasm 解释执行各通过 47 项检查；managed Wasm AOT、其他作者主机 / 浏览器及发布打包按各自证据验收。详见[调用流程](testing.md#independent-webassembly-invocation)、[本次验收](wasm-acceptance-2026-10-03.cn.md)和[目标证据](capabilities.cn.md#目标证据)。

## 开放目标与工具链

`BGCS.Core.Targeting` 管理开放目标 ID、请求、不可变目标与工具链描述和 provider 契约；Core 不引用 Parser。Parser 的 resolver 拒绝未支持或重复认领的目标；空 provider 集合不会隐式恢复默认实现。

目标 SDK headers 与 libclang builtin headers 分开管理。Emscripten provider 从显式选择的 sysroot 提供 libc include，不猜测 Windows 路径或消费方 runtime。Apple 设备与模拟器保持独立目标身份。输入与生成缓存按具体工具链、目标 ABI 和源内容指纹隔离。
