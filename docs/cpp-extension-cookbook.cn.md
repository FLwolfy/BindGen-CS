# C++ 扩展实战手册

[English](cpp-extension-cookbook.md) | [Wiki](README.cn.md) | [Lowering 架构](lowering.cn.md)

本文解决一个具体问题：当 BGCS 的内置 C/C++ 支持不足以表达项目语义时，如何在**不修改 BGCS core、不手改生成文件**的前提下完成 binding。

仓库内有两个可直接构建的示例：

- [`examples/NativeShim`](../examples/NativeShim/README.md)：生成 bridge、编译 native library，并从 C# 调用；
- [`examples/LoweringPlugin`](../examples/LoweringPlugin/README.md)：独立 typed lowering plugin 项目。

## 先选对扩展层级

| 问题 | 选择 | 原因 |
| --- | --- | --- |
| BGCS 已内置 string/container/smart-pointer/path/chrono 等语义 | 不扩展 | 使用 verified built-in |
| 类型能用确定的 C ABI 类型和表达式转换 | `TypeLowerings` | 只需 JSON，可审查、可缓存 |
| 函数只需匹配、重命名、排除或包装调用表达式 | `CallableLowerings` | 只需 JSON，不执行任意代码 |
| 需要读取 AST、按 target 分支或生成额外 artifact | typed lowering plugin | 独立受信任 .NET assembly |
| 只有项目 C++ 才能解释模板、coroutine、private ABI 或 lifetime | `NativeShims` | 明确建立稳定 C ABI 边界 |
| 规则能生成，但 BGCS 无法证明项目断言 | `AllowUserAsserted` | 接受经过项目审查的 recipe/plugin/shim |
| 项目决定承担未证明风险 | `AllowUnsafe` | 只绕过安全 gate，并保留审计诊断 |

决策顺序：

```text
内置 lowering 能表达？ ──是──> 直接生成
        │否
        ▼
确定性 JSON 模板能表达？ ──是──> TypeLowerings / CallableLowerings
        │否
        ▼
需要 AST/target/artifact 逻辑？ ──是──> typed lowering plugin
        │否或仍不足
        ▼
能写稳定 C ABI wrapper？ ──是──> NativeShims
        │否
        ▼
该能力当前不可绑定；必须先改变上游公开 API 或 native 设计
```

`AllowUnsafe` 不在这棵树中替代任何扩展层。它不会创建转换、实例化模板、修正 calling convention 或延长对象生命周期。

## 声明式 TypeLowerings

项目值类型能稳定映射到一个 C ABI carrier 时使用：

```json
{
  "LoweringSafetyPolicy": "AllowUserAsserted",
  "TypeLowerings": [
    {
      "Name": "engine.entity-id",
      "TypePattern": "Engine::EntityId",
      "Priority": 100,
      "Kind": "Custom",
      "CAbiType": "uint64_t",
      "Marshalling": "Blittable",
      "Ownership": "Borrowed",
      "AbiShape": "Direct",
      "ParameterToCppExpression": "Engine::EntityId::FromRaw({value})",
      "ReturnToCExpression": "({value}).Raw()",
      "RequiredHeaders": ["Engine/EntityId.hpp"],
      "ManagedProjection": {
        "ManagedType": "EntityId",
        "ManagedToNativeExpression": "{value}.Value",
        "NativeToManagedExpression": "new EntityId({value})",
        "RequiredNamespace": "Engine.Managed"
      },
      "Safety": "UserAsserted"
    }
  ]
}
```

`TypePattern` 是 ordinal glob，支持 `*`。表达式占位符为 `{value}`、`{name}`、`{count}`、`{cppType}`。如果一个 native 参数必须展开为 pointer + length，可以配置 `AbiParameters`；如果转换需要控制流、共享状态或动态 AST 判断，应升级为 plugin 或 shim。

## 完整 CallableLowerings 示例

下面同时演示 fully-qualified glob、稳定 export rename、调用包装、额外 include 和排除内部函数：

```json
{
  "LoweringSafetyPolicy": "AllowUserAsserted",
  "CallableLowerings": [
    {
      "Name": "engine.checked-add",
      "FunctionPattern": "Engine::Math::Add",
      "Priority": 200,
      "ExportName": "engine_math_checked_add",
      "Exclude": false,
      "InvocationExpression": "Engine::Interop::Check({invocation})",
      "RequiredHeaders": ["Engine/Interop/Check.hpp"],
      "Safety": "UserAsserted"
    },
    {
      "Name": "engine.hide-debug",
      "FunctionPattern": "Engine::*::Debug*",
      "Priority": 100,
      "Exclude": true,
      "Safety": "UserAsserted"
    }
  ]
}
```

规则：

- `FunctionPattern` 同时匹配 fully-qualified name 和短函数名，支持 `*`；
- `ExportName` 必须是稳定且不冲突的 C symbol；
- `InvocationExpression` 必须包含 `{invocation}`，替换后仍须返回原 callable 所需的 native 结果；
- `RequiredHeaders` 是生成 wrapper 编译所需的 header；
- `Exclude=true` 不生成该 callable；
- callable recipe 不能掩盖非法参数/返回 ABI，类型仍必须先被内置或 type lowering 接受。

验证：

```bash
bindgen-cs bridge bridge.json
bindgen-cs native-build GeneratedBridge/bridge.manifest.json
```

`bridge` 会在事务提交前完成 C++ 配置、解析与 lowering 验证；失败时不会用部分结果替换正式输出。

## 独立 typed lowering plugin

当 JSON 不足以做 AST、target 或 artifact 决策时，建立独立项目；不要修改 BGCS core。

### 目录

```text
LoweringPlugin/
├─ BGCS.Example.LoweringPlugin.csproj
└─ EngineLoweringPlugin.cs
```

完整可编译项目位于[`examples/LoweringPlugin`](../examples/LoweringPlugin/README.md)。仓库内 `.csproj` 使用 project reference：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/BGCS.Core/BGCS.Core.csproj" />
    <ProjectReference Include="../../src/BGCS.CppAst/BGCS.CppAst.csproj" />
    <ProjectReference Include="../../src/BGCS.Cpp2C/BGCS.Cpp2C.csproj" />
  </ItemGroup>
</Project>
```

Plugin entry point 必须是 public、可无参构造：

```csharp
using BGCS.Core.Extensibility;
using BGCS.Cpp2C.Lowering;
using BGCS.CppAst.Model.Declarations;

public sealed class EngineLoweringPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    public string Id => "engine.lowering";
    public string Version => "1.0.0";
    public int ContractVersion => BindingPluginContract.CurrentVersion;

    public void Configure(IBindingPluginHost host) =>
        host.Register<ICppCallableLowering>(
            "engine.rename-tick",
            new RenameTickLowering(),
            priority: 100);

    public string GetCacheFingerprint() => "rename-tick";

    private sealed class RenameTickLowering : ICppCallableLowering, ICacheFingerprintProvider
    {
        public string Name => "engine.rename-tick";
        public int Priority => 100;

        public bool CanLower(CppFunction function, CppCallableLoweringContext context) =>
            function.Name == "Tick" && context.DeclaringType?.FullName == "Engine::World";

        public CppCallableLoweringPlan CreatePlan(
            CppFunction function,
            CppCallableLoweringContext context) =>
            new(Name, "engine_world_tick");

        public string GetCacheFingerprint() => "engine-world-tick";
    }
}
```

构建并加载：

```bash
dotnet build examples/LoweringPlugin/BGCS.Example.LoweringPlugin.csproj --configuration Release
```

```json
{
  "PluginAssemblies": [
    "examples/LoweringPlugin/bin/Release/net9.0/BGCS.Example.LoweringPlugin.dll"
  ]
}
```

路径相对于包含它的配置文件。Plugin 可以注册：

- `ICppTypeLowering`：决定 C ABI shape、conversion、ownership、allocator、cleanup 和 managed projection；
- `ICppCallableLowering`：选择、排除、重命名或包装 callable；
- `ICppArtifactContributor`：确定性生成 public header、native source、managed source 或 resource。

Plugin 是构建期间执行的受信任代码，必须 pin binary、代码审查并在 CI 构建。所有影响输出的状态应实现 `ICacheFingerprintProvider`；否则 BGCS 会保守关闭 cache hit。`ContractVersion` 返回当前常量只是加载时不兼容保护，不是“v1/v2 plugin”产品代际。

## 完整 native shim 教程

Shim 是最通用的 escape hatch：项目自己把复杂 C++ 语义收敛成小而稳定的 C ABI，BGCS 负责复制、构建 manifest、export inspection 和 C# binding。

### 示例目录

```text
examples/NativeShim/
├─ bridge.json
├─ native/
│  ├─ entry.hpp
│  └─ library.hpp
├─ shims/
│  ├─ widget_c.h
│  └─ widget_c.cpp
└─ Consumer/
   ├─ Consumer.csproj
   └─ Program.cs
```

`native/library.hpp` 代表不直接暴露给 C# 的项目 C++ API。`shims/widget_c.h` 只暴露稳定 C ABI：

```cpp
#pragma once
#include "common.h"
#include <stddef.h>

API(void*) bgcs_widget_create(void);
API(void) bgcs_widget_destroy(void* widget);
API(int) bgcs_widget_sum(void* widget, const int* values, size_t count, int* result);
```

`shims/widget_c.cpp` 负责对象 lifetime、参数验证和异常边界：

```cpp
#include "widget_c.h"
#include "library.hpp"
#include <new>

API_INTERNAL(void*) bgcs_widget_create(void)
{
    return new (std::nothrow) Example::Widget();
}

API_INTERNAL(void) bgcs_widget_destroy(void* widget)
{
    delete static_cast<Example::Widget*>(widget);
}

API_INTERNAL(int) bgcs_widget_sum(
    void* widget,
    const int* values,
    size_t count,
    int* result)
{
    if (widget == nullptr || result == nullptr || (values == nullptr && count != 0))
        return -1;
    try
    {
        *result = static_cast<Example::Widget*>(widget)->Sum(values, count);
        return 0;
    }
    catch (...)
    {
        return -2;
    }
}
```

`bridge.json`：

```json
{
  "ConfigVersion": 1,
  "EntryFiles": ["native/entry.hpp"],
  "AllowedHeaders": ["native/entry.hpp"],
  "IncludeFolders": ["native"],
  "OutputPath": "GeneratedBridge",
  "LanguageStandard": "c++23",
  "GenerateBuildManifest": true,
  "GenerateCSharpBindings": true,
  "CSharpNamespace": "BGCS.Examples.NativeShim",
  "CSharpApiName": "WidgetNative",
  "NativeLibraryName": "widget_bridge",
  "CSharpOutputPath": "Generated",
  "LoweringSafetyPolicy": "AllowUserAsserted",
  "NativeShims": [
    {
      "Name": "widget",
      "PublicHeaders": ["shims/widget_c.h"],
      "SourceFiles": ["shims/widget_c.cpp"],
      "Safety": "UserAsserted"
    }
  ]
}
```

生成：

```bash
cd examples/NativeShim
bindgen-cs bridge bridge.json
```

这会产生：

- `GeneratedBridge/include/extensions/widget/widget_c.h`；
- `GeneratedBridge/src/extensions/widget/widget_c.cpp`；
- 含 shim source/include 的 `GeneratedBridge/bridge.manifest.json`；
- 含 `WidgetNative.BgcsWidget*` 方法的 `Generated/Bindings.cs`。

编译 native library。选择当前平台对应输出名：

```bash
# Windows
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --output Consumer/bin/Debug/net9.0/widget_bridge.dll

# Linux
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --output Consumer/bin/Debug/net9.0/libwidget_bridge.so

# macOS
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --output Consumer/bin/Debug/net9.0/libwidget_bridge.dylib
```

调用生成的 C# API：

```csharp
using BGCS.Examples.NativeShim;

unsafe
{
    void* widget = WidgetNative.BgcsWidgetCreate();
    if (widget == null)
        throw new InvalidOperationException("Unable to allocate the native widget.");

    try
    {
        int[] values = [10, 20, 12];
        fixed (int* pointer = values)
        {
            int result = 0;
            int status = WidgetNative.BgcsWidgetSum(
                widget,
                pointer,
                (ulong)values.Length,
                &result);
            if (status != 0)
                throw new InvalidOperationException($"Native status: {status}");
            Console.WriteLine(result);
        }
    }
    finally
    {
        WidgetNative.BgcsWidgetDestroy(widget);
    }
}
```

```bash
dotnet run --project Consumer/Consumer.csproj
# 42
```

`native-build` 默认检查真实动态库 export，不需要手写额外 export list。用于 NuGet 时改用 `--package-root artifacts/package`，BGCS 会写入 `runtimes/<rid>/native/` 和带 SHA-256 的资产索引。

## Ownership 与 allocator 推荐模式

### 规则

1. 谁分配，谁释放；不得让 .NET 的 `Marshal.FreeHGlobal` 释放 native `new/malloc/custom allocator` 内存。
2. 每个 owning handle 都提供一个幂等语义明确的 destroy/release 函数。
3. Borrowed pointer 必须写明有效期；不能超过 owner、下一次 mutation 或 callback frame。
4. 返回 buffer 时同时返回 length，并提供同模块 release；不要依赖 null terminator 表达任意二进制数据。
5. C++ exception 永远不能跨 C ABI；转成 status code 和可复制 error message。

推荐 ABI：

```c
typedef struct bgcs_blob {
    unsigned char* data;
    size_t length;
} bgcs_blob;

API(int) engine_read_blob(void* owner, bgcs_blob* result);
API(void) engine_release_blob(bgcs_blob* value);
```

`engine_release_blob` 必须使用与 `engine_read_blob` 相同模块、相同 allocator family，并把 `data/length` 清零以降低 double-release 风险。Managed friendly layer 应把 owner 包装为 `SafeHandle`；临时 buffer 在 `try/finally` 中释放。

如果 allocator 是项目可配置的，把 allocator/deallocator 作为成对 contract 建模；只知道 allocate 而不知道 free 时应拒绝生成 owning friendly API。

## Callback lifetime 推荐模式

推荐注册协议：

```c
typedef void (CALL *engine_event_callback)(void* context, int event_code);

API(unsigned long long) engine_register_event(
    void* owner,
    engine_event_callback callback,
    void* context);
API(void) engine_unregister_event(void* owner, unsigned long long token);
```

契约必须明确：

- 返回 token，不用 callback address 作为身份；
- `unregister` 返回后不再开始新的 callback；
- 如果允许并发 callback，`unregister` 是等待 in-flight callback 结束的 barrier，或另有明确 drain API；
- native 保存 `context` 但不拥有它；managed 侧用 `GCHandle`/root 保持 delegate 与状态，直到 unregister barrier 完成；
- callback 线程、重入、异常处理和 dispose race 必须有测试；
- managed exception 不能穿过 unmanaged frame，应捕获并转成项目错误通道。

如果 native API 无法保证 unregister barrier，安全包装必须使用共享 ref-counted registration state；单纯 `Dispose` 后立即释放 delegate 是 use-after-free。

## Async completion 推荐模式

推荐 operation handle，而不是把 stack pointer 或短生命周期 delegate 交给后台线程：

```c
typedef void (CALL *engine_async_completion)(
    void* context,
    int status,
    const unsigned char* data,
    size_t length);

API(void*) engine_begin_load(
    const char* path,
    engine_async_completion completion,
    void* context);
API(void) engine_cancel_load(void* operation);
API(void) engine_release_operation(void* operation);
```

必须定义：

- completion 最多一次，或者明确允许的 progress + terminal 状态机；
- `cancel` 是请求还是完成 barrier；通常 cancel 后仍等待 terminal completion；
- completion 的 buffer 只在 callback frame 有效，或另有 owning release API；
- operation 只能在 terminal completion/drain 后 release；
- managed 侧用 `TaskCompletionSource`、rooted callback 和 cancellation registration，并原子处理 completion/cancel/dispose 竞争；
- shutdown 时有 drain/join，不能卸载 native library 后继续 callback。

## `AllowUnsafe` 的正确位置

安全策略：

| Policy | 接受内容 | 推荐用途 |
| --- | --- | --- |
| `VerifiedOnly` | BGCS 内置验证规则 | 默认、第三方未知输入 |
| `AllowUserAsserted` | 项目审查过的 recipe/plugin/shim | 正常项目扩展 |
| `AllowUnsafe` | 标记为 unsafe 的显式扩展，并产生 `BGCS-SAFETY-LOWERING-BYPASS` | 临时迁移或外部审计明确承担风险 |

选择 `AllowUnsafe` 后至少保留：native compile、export inspection、真实 invocation、错误路径、allocator、dispose race 和 deterministic regeneration 测试。不要为了“让生成继续”把所有配置全局设为 unsafe。

## 完成检查表

- [ ] 没有修改 `Generated/` 内文件；clean regeneration 后 diff 稳定。
- [ ] recipe/plugin/shim 只包含项目语义，没有 native library 名称特判进入 BGCS core。
- [ ] owning value 有唯一释放路径，borrowed value 有明确有效期。
- [ ] callback 有 unregister/drain，async 有 terminal/drain。
- [ ] exception 不跨 C ABI，status/error channel 可测试。
- [ ] native library 通过 export inspection 和真实 invocation。
- [ ] plugin/shim 输入进入 cache fingerprint。
- [ ] 每个宣称支持的平台都有独立实机报告。
