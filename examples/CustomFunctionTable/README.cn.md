# 外部原生槽位与仅依赖 Runtime 的适配器

原生端提供七个槽位：`table_add` 位于 6，`table_multiply` 位于 2，
`table_hold` 位于 4。0、1、3、5 是保留槽，可以为 null。
绑定函数本身不导出为 DLL 符号，原生模块只导出 bootstrap 协议。

安装 .NET 10、BGCS 工具、CMake 和原生 C 工具链后，在本目录执行：

```sh
bindgen-cs generate bindgen.json
cmake -S . -B artifacts/native
cmake --build artifacts/native --config Release
dotnet run --project Consumer -- /absolute/path/to/custom_table.dll
```

请传入实际产物路径。Windows 常见输出为
`artifacts/native/Release/custom_table.dll`，Linux 为
`artifacts/native/libcustom_table.so`，macOS 为
`artifacts/native/libcustom_table.dylib`。

`Consumer` 只引用 `BGCS.Runtime`，编译生成绑定与小型 `CustomSlotTable`
适配器。适配器核对原生身份、完整表长度、指针宽度和函数槽宽度，选择三个
具名地址，再将 `ExternalFunctionTableProvider` 交给
`CustomTableApi.FromProvider`。生成器不需要知道外部表的槽位位置。

Provider 复制已核对的名称和地址，要求所有具名 callable 非零，并在构造成功后
接收模块引用的所有权。生成上下文也会先解析完整必需符号集，再暴露 API。
适配失败时，适配器关闭自己打开的引用；转移成功后应释放上下文，不能在调用
或资源仍存活时单独释放其 provider。调用 lease 使原生调用返回前始终保留模块，
包括原生调用反向进入托管 callback 的情况。

身份字符串是签名和表布局的显式约定，相等本身不证明 ABI 正确。原生端必须
以匹配目标和调用约定实现 `native.h`。本例约定 C ABI、32 位 `int` 和指针宽度的
函数槽；槽宽度不同会被拒绝。宽度相等本身不能证明函数指针表示兼容；适配器
契约还要求目标运行时能够把槽内容解释为可直接调用的地址。仓库的真实生成消费者测试独立编译
原生端，通过非导出的函数槽执行调用，核对失败、表复制，以及原生调用阻塞在
callback 时关闭上下文的生命周期行为。

`BGCS_CUSTOM_TABLE_TEST_FAULTS` 仅为测试开启 bootstrap 故障注入；
示例 CMake 工程没有启用它。
