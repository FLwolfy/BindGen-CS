# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

**BindGen-CS（BGCS）生成让 C# 调用 C / C++ 库的绑定代码。**
你提供原生头文件和配置，BGCS 生成 C# bindings。对于 C++ API，
它还能生成 C bridge，将类和支持的模板转换为 C# 可以调用的 C ABI。

## 先理解三个部分

| 部分 | 职责 | 来源 |
| --- | --- | --- |
| 头文件（`.h` / `.hpp`） | 声明原生函数与类型 | 原生库 |
| 原生库（`.dll`、`.so`、`.dylib` 或链接进 Wasm 的代码） | 实现这些函数 | 你的原生构建流程 |
| C# 绑定（`Bindings.cs`） | 按正确 ABI 从 C# 调用这些函数 | BGCS 生成 |

生成 bindings 不会编译原生库、把库的实现翻译成 C#，也不会部署你的应用。
C# bindings 程序集与原生 DLL 是不同的两部分。

```text
C API：   头文件 ─────────────> C# bindings ──> 调用原生库
C++ API： 头文件 ──> C bridge ─> C# bindings ──> 调用编译后的 bridge
```

## 快速开始

### 1. 准备环境

从源码使用本仓库，需要 **.NET SDK 10.0** 和 **.NET 9 runtime**。
根目录 `global.json` 选择 SDK 10，各项目的目标框架为 `net9.0`。
用 `dotnet --list-sdks`、`dotnet --list-runtimes` 检查安装情况。

编译原生库或 C++ bridge 时还需要原生编译器和目标平台 SDK。
BGCS 随解析器提供 Clang builtin headers；它们是编译器支持文件，
不能替代你的目标 SDK 或 C / C++ 标准库。

### 2. 第一次生成 bindings

在仓库根目录打开终端，执行：

```bash
dotnet run --project src/BGCS.Tool -- init examples/QuickStart/native.h --config examples/QuickStart/bindgen.json
dotnet run --project src/BGCS.Tool -- generate examples/QuickStart/bindgen.json
dotnet run --project src/BGCS.Tool -- build examples/QuickStart/bindgen.json
```

- `init` 创建初始配置。新接入一个库时执行一次。
- `generate` 输出 `examples/QuickStart/Generated/Bindings.cs`。
- `build` 生成 bindings，并在临时消费项目中编译检查 C#。
  **它不会编译原生库，也不代表原生调用已经运行成功。**

示例头文件声明了 `bgcs_add`。接入自己的库时，换成对应头文件并检查配置。
生成目录属于可重建产物；命名、类型和封送等调整写在配置里，不手改 `Bindings.cs`。

### 3. 在 C# 项目中使用

1. 把生成的 `Bindings.cs` 纳入项目编译。
2. 引用 `BGCS.Runtime`。从源码接入时，添加到
   `src/BGCS.Runtime/BGCS.Runtime.csproj` 的项目引用；使用已发布包时可改为包引用。
3. 为应用目标平台编译并部署原生库。配置中的 `libName` 要与可加载库名对应，
   原生函数也必须正确导出。
4. 调用生成的 API，并用真实原生库验证结果。

默认示例配置对应的调用如下，**执行前需要提供原生实现**：

```csharp
using Native.Bindings;

int result = NativeApi.BgcsAdd(2, 3);
```

初始配置使用 `Native.Bindings`、`NativeApi` 和库名 `native`。
可按项目需要修改 `namespace`、`apiName`、`libName`。
完整接入步骤与排错方式见[快速开始](docs/getting-started.cn.md)。

消费项目应启用 `<DisableRuntimeMarshalling>true</DisableRuntimeMarshalling>`。
生成的 wrapper 已明确实现 ABI 转换；该设置让 .NET 按声明调用原生函数，避免运行时重复封送。
它适用于各目标，并不是浏览器专用选项。还需运行实际 API，不能仅凭生成成功或 `sizeof` 相等判断调用正确。

## 接入 C++ 库

C++ 类通常不能直接按 C ABI 调用。BGCS 可以为支持的 C++ 语义生成 bridge 与 bindings：

```bash
dotnet run --project src/BGCS.Tool -- init path/to/library.hpp
dotnet run --project src/BGCS.Tool -- bridge bridge.json
dotnet run --project src/BGCS.Tool -- native-build GeneratedBridge/bridge.manifest.json
```

`GeneratedBridge/` 保存 C 头文件、C++ wrapper 和原生构建清单；
`Generated/` 保存配套 C# bindings。bridge 配置还需包含原生库所需的源码、
链接库和 include 路径。`native-build` 根据清单编译 bridge 并验证导出符号。

支持的 lowering 包括类、重载、继承、显式配置的模板实例、常见 STL 容器、
智能指针和配置式 callback proxy。未知 C++ 语义会被拒绝，
需要通过 lowering 或项目自己的 shim 明确转换。
详见[能力与边界](docs/capabilities.cn.md)和[C++ 扩展实战手册](docs/cpp-extension-cookbook.cn.md)。

## 跨平台与 WebAssembly

**运行 BGCS 的机器与运行最终应用的平台是两个独立概念。**
Windows、Linux、macOS 主机可以为显式选择的目标生成 bindings，
前提是目标 SDK 和主机解析器 runtime 可用。原生库仍需按目标分别构建。
指针宽度、`long`、结构体 packing、调用约定和条件声明都可能影响 ABI；
一个平台运行成功不能替代另一个平台的验收。

| 场景 | 需要配置 |
| --- | --- |
| 桌面原生库 | 目标 ABI、编译器 / SDK 路径及库名 |
| 通过 Emscripten 输出 WebAssembly | `emscripten-c` / `emscripten-cpp` preset 和目标 SDK / sysroot |
| 其他 WebAssembly 环境 | 对应目标与 runtime 契约；WASI 不是 Emscripten 的别名 |

Emscripten 复用桌面目标同一套 parser、analysis、IR 和 emitter。
BGCS 负责生成 bindings；应用工具链负责将原生代码编译、链接进 Wasm 并提供浏览器启动。
BGCS 不依赖任何游戏引擎。

独立的 [Wasm 调用测试](docs/testing.md#independent-webassembly-invocation)
会生成三种 import mode，在真实浏览器中调用 BGCS 自有 C fixture，
检查返回值、布局、缓冲区、opaque handle、回调和资源释放。
已验收主机与待完成范围见[目标证据](docs/capabilities.cn.md#目标证据)。
桌面、移动端、打包和浏览器覆盖各有独立验收边界。

## 导入方式、安全与扩展

- **`DllImport`**：传统 P/Invoke 声明。
- **`LibraryImport`**：由 .NET source generator 实现的 P/Invoke 声明。
- **`FunctionTable`**：通过解析后的函数指针调用。
  可用 `INativeContext` 接入应用自己的符号解析；它不会让不支持动态加载的平台获得动态加载能力。

BGCS 保留 raw ABI，并在安全契约明确时生成方便使用的 `string`、`Span<T>`、`ref`、`out` overload。
缺少 ownership、allocator、buffer length 或 callback lifetime 信息时，
默认产生诊断并抑制无法证明安全的友好 overload，也可以配置为拒绝整次生成。

项目特有语义通过 `TypeLowerings` / `CallableLowerings`、typed lowering plugin 或 native shim 扩展，
不应写成 BGCS 核心里的库专用分支。详见[配置指南](docs/configuration-guide.cn.md)、
[诊断指南](docs/diagnostics.cn.md)和[架构说明](docs/architecture.cn.md)。

## 常用命令

源码使用方式是 `dotnet run --project src/BGCS.Tool -- <command>`。
安装已发布的 `BindGen-CS` Tool 包后，可使用 `bindgen-cs <command>`。

| 命令 | 用途 |
| --- | --- |
| `doctor` | 检查主机 compiler、include 和 SDK |
| `validate` / `inspect` | 检查配置或查看分析后的 API |
| `generate` / `build` | 生成 C#，或额外编译检查 |
| `bridge` / `native-build` | 生成并编译 C++ bridge |
| `diff` | 检查生成代码是否需要更新 |
| `workspace` | 统一处理多个原生库 |
| `schema` / `explain` | 导出配置 schema 或解释诊断 |

嵌入构建工具时引用 `BGCS` 或 `BGCS.Cpp2C`；消费生成代码通常只需 `BGCS.Runtime`。
`BGCS.Intermediate` 提供独立 IR 契约。详见[包与公开 API](docs/packages.cn.md)。

## 测试与文档

```bash
dotnet test BindGen-CS.sln -c Release
python scripts/test-wasm-bindings.py
python scripts/test-wasm-bindings.py --aot
python scripts/test-native-aot-bindings.py
```

Wasm 测试额外需要 **.NET 9 SDK + `wasm-tools` workload**、Python 3.10+ 和 Chrome、Chromium 或 Edge。
可通过 `--dotnet`、`--browser` 指定已安装的可执行文件。
缺少依赖或原生调用未完整成功时，测试会失败，不会静默跳过。

完整目标发布检查执行 `bash scripts/run-full-test-matrix.sh`。
原生 ABI、确定性快照、NuGet 消费、供应链与性能检查，均有独立验收范围。
详见[测试流程](docs/testing.md)和[验收规范](docs/acceptance.cn.md)。

- [文档入口](docs/README.cn.md)
- [第一次接入](docs/getting-started.cn.md)
- [配置参考](docs/configuration-guide.cn.md)
- [能力与平台证据](docs/capabilities.cn.md)
- [C++ 扩展实战手册](docs/cpp-extension-cookbook.cn.md)
- [发布说明](docs/publish.cn.md)

## License

BGCS 使用 [MIT License](LICENSE)，派生自 CppAst / HexaGen 的部分保留原始声明。
随解析器提供的 Clang builtin headers 使用 Apache-2.0 WITH LLVM-exception，
其[来源、校验值与许可](extern/clang-resource/README.md)包含在 parser 包中。

### 独立原生调用验证

同一 fixture 在 Wasm 解释执行、Wasm AOT 和桌面 NativeAOT 中测试三个 import mode，共 47 项检查。它包含 BGCS 自有 C API 和经公开生成流程产生的 C++ bridge，覆盖数据布局、bool、原生 long、回调、构造、继承指针调整、销毁与 owned/borrowed 资源清理。`--inject-native-error` 用于确认错误返回值确实导致验收失败。

这些消费者启用 `DisableRuntimeMarshalling`，以符合 .NET source-generated P/Invoke 对 unmanaged wrapper struct 的要求。完整过程与证据边界见[测试说明](docs/testing.md)。
