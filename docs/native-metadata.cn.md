# 原生名称属性

生成的原生文件、输入根目录和消费者源码选择见[可迁移原生源码输入](native-source-inputs.cn.md)。
该类型化清单与本页的可选 C# 属性互相独立。

将 `api.emitNativeMetadata` 设为 `true`，即可给规范的 `RawApi` 方法及对应的 DllImport/LibraryImport 声明添加原生名称属性。默认值为 `false`。

属性保存原生函数身份、最终 C ABI 的返回类型拼写，以及每个原生参数的名字和类型拼写。修改托管名称不会改变这些事实。FunctionTable 使用相同的规范 RawApi 注解。本选项不覆盖转换后的 friendly 方法、别名、类型或字段声明，也不覆盖非拥有型指针视图。

```csharp
var method = typeof(NativeApi.RawApi).GetMethod("SampleAdd")!;
var names = method.GetCustomAttributes(
    typeof(BGCS.Runtime.NativeNameAttribute), inherit: false);
var nativeReturn = method.ReturnParameter.GetCustomAttributes(
    typeof(BGCS.Runtime.NativeNameAttribute), inherit: false);
```

`NativeNameType.Func`、`Type` 和 `Param` 区分函数、返回值或参数类型，以及参数名称。对于 C++ bridge，函数身份可以保留最初的限定源码名称，而类型拼写描述经过检查的最终 C ABI，包括新增的状态码和结果通道。这些属性不能证明调用 ABI 兼容，也不表示资源所有权。

backend 从不可变 interop 事实创建类型化元数据计划，使用语法节点和转义后的字符串字面量生成属性。最终产物验证先检查属性身份、值、target、数量和位置，仅移除已经独立验证的属性，再执行原有 ABI 检查。后处理器不能修改这些元数据，也不能借此隐藏其他属性或原生签名的变化。

Runtime 包使用 `BGCS.Runtime.NativeNameAttribute`。源码模式将闭合的 `metadata.native-names` 功能放入所选 Runtime namespace，也支持单文件输出。关闭选项会同时省略这些属性及其 Runtime 源码功能。产物清单与 ABI JSON 元数据独立存在。

对应的回归源码位于 `tests/BGCS.CSharp.Tests/NativeMetadataTests.cs` 和 `tests/BGCS.Tests/NativeMetadataTests.cs`。平台认证仍要求同 revision 的真实执行收据。

## 外部编译

BGCS 生成 C# 绑定，并为支持的 C++ 声明生成 C bridge 源码。原生编译、链接、导出检查、打包与部署由使用者的构建系统负责；托管源码由消费者工程编译。生成成功不代表这些后续步骤或真实调用已经通过。

运行 `bindgen-cs generate bindgen.json`，仅需要 bridge 时使用 `bridge`。通过 `GenerationResult.nativeSources` 或 `BindingArtifactManifest.ReadNativeSources` 选择确切目标的生成翻译单元、shim 实现和有序 include 要求，按[输入映射说明](native-source-inputs.cn.md)映射逻辑根。将所选文件与上游实现一起加入自己的 CMake、Meson、MSBuild 或编译器工程。保持架构、调用约定、packing、语言标准、宏、include、SDK 与 C++ ABI 一致，显式链接上游库及依赖。生成头文件不提供缺失的实现。

QuickStart 的原生工程独立构建：

```bash
cmake -S examples/QuickStart -B artifacts/quickstart-native
cmake --build artifacts/quickstart-native --config Release
bindgen-cs generate examples/QuickStart/bindgen.json
```

消费者启用 unsafe，引用 `BGCS.Runtime` 或生成的 Runtime 源码，再编译 `managed` 文件。将自建库的绝对路径交给 `NativeApi.Load(...)`；浏览器、NativeAOT 和 Apple 静态输入由应用工程的普通原生链接项显式配置。

所有 import 策略（包括静态 adapter 的导出表导入）均保留 `library.nativeName`，它必须匹配消费者注册的模块名。.NET 10 浏览器工程通过 `NativeFileReference` 的文件名确定 P/Invoke 模块名，例如 `api.c` 或 `api.a` 对应 `api`，此时应将 `nativeName` 配置为 `api`。只有消费者注册了 `__Internal` 时才显式选择该名称。静态 adapter 决定调用与生命周期策略，不负责注册或重命名原生模块。

仓库 `scripts/native_fixture_build.py` 仅用于验收 fixture，不随 SDK / CLI 交付，也不是原生 provider API。真实消费验收独立构建原生代码、编译生成的 C# 并执行调用；解析与布局检查不能替代该证据。
