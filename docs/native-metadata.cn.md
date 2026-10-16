# 原生名称属性

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
