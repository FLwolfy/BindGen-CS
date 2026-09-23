# C++ extension cookbook

[简体中文](cpp-extension-cookbook.cn.md) | [Wiki](README.md) | [Lowering architecture](lowering.md)

This guide shows how to bind project-specific semantics **without modifying BGCS core or hand-editing generated output**. Runnable sources live in [`examples/NativeShim`](../examples/NativeShim/README.md) and [`examples/LoweringPlugin`](../examples/LoweringPlugin/README.md).

## Choose the smallest extension

| Question | Choose | Why |
| --- | --- | --- |
| A verified built-in already covers the type | Nothing | Keep the built-in safety evidence |
| A deterministic C ABI type and expressions describe the conversion | `TypeLowerings` | Reviewable JSON with deterministic caching |
| A callable only needs matching, renaming, exclusion, or expression wrapping | `CallableLowerings` | JSON; no arbitrary code execution |
| The decision needs AST inspection, target branches, or generated artifacts | Typed lowering plugin | Independent trusted .NET assembly |
| Only project C++ can interpret the template/coroutine/private ABI/lifetime | `NativeShims` | Establish an explicit stable C ABI |
| BGCS cannot prove a reviewed project assertion | `AllowUserAsserted` | Accept a reviewed recipe/plugin/shim |
| The project deliberately owns an unproven risk | `AllowUnsafe` | Bypass only the safety gate and retain diagnostics |

```text
Verified built-in? ──yes──> Generate
       │ no
       ▼
Deterministic JSON templates? ──yes──> TypeLowerings / CallableLowerings
       │ no
       ▼
Need AST/target/artifact logic? ──yes──> typed lowering plugin
       │ no or still insufficient
       ▼
Can expose a stable C ABI? ──yes──> NativeShims
       │ no
       ▼
Not currently bindable; change the upstream public/native design first
```

`AllowUnsafe` is not an alternative branch. It cannot invent a conversion, instantiate a template, repair a calling convention, or extend an object's lifetime.

## Declarative type lowering

Use a type recipe when a project value has a stable C ABI carrier:

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

`TypePattern` is an ordinal glob with `*`. Expression placeholders are `{value}`, `{name}`, `{count}`, and `{cppType}`. Use `AbiParameters` for pointer-plus-length expansion. Move to a plugin or shim when conversion needs control flow, shared state, or dynamic AST decisions.

## Complete `CallableLowerings` example

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

`FunctionPattern` matches both the fully qualified and short name and accepts `*`. `ExportName` must be a stable, unique C symbol. `InvocationExpression` must contain `{invocation}` and still produce the native result required by the callable. `RequiredHeaders` supplies wrapper compilation dependencies. `Exclude=true` omits the callable. A callable recipe cannot hide an invalid parameter or return ABI; every type still needs an accepted lowering.

```bash
bindgen-cs bridge bridge.json
bindgen-cs native-build GeneratedBridge/bridge.manifest.json
```

`bridge` validates C++ configuration, parsing, and lowering before the output transaction commits; a failed run does not replace final output with partial files.

## Independent typed lowering plugin

Use a separate project when JSON cannot express the AST, target, or artifact decision. The complete buildable project is [`examples/LoweringPlugin`](../examples/LoweringPlugin/README.md).

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

Paths are relative to the configuration file. Plugins can register `ICppTypeLowering`, `ICppCallableLowering`, and `ICppArtifactContributor`. They execute trusted build-time code: pin and review the binary and build it in CI. Output-affecting state should implement `ICacheFingerprintProvider`; otherwise BGCS conservatively disables cache hits. Returning `BindingPluginContract.CurrentVersion` is a load-time compatibility guard, not a marketed “v1/v2 plugin” generation.

## Complete native shim tutorial

A shim is the universal escape hatch: project-owned C++ reduces complex semantics to a small stable C ABI, while BGCS owns copying, the build manifest, export inspection, and C# binding generation.

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

The public shim header is deliberately C-shaped:

```cpp
#pragma once
#include "common.h"
#include <stddef.h>

API(void*) bgcs_widget_create(void);
API(void) bgcs_widget_destroy(void* widget);
API(int) bgcs_widget_sum(void* widget, const int* values, size_t count, int* result);
```

The implementation owns lifetime, validation, and the exception boundary:

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

The complete [`bridge.json`](../examples/NativeShim/bridge.json) enables C# output and declares the project-owned files:

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

```bash
cd examples/NativeShim
bindgen-cs bridge bridge.json
```

The result includes copied extension sources, a manifest containing those sources and include paths, and `Generated/Bindings.cs` with `WidgetNative.BgcsWidget*` methods.

Build the platform library:

```bash
# Windows
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --output Consumer/bin/Debug/net9.0/widget_bridge.dll

# Linux
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --output Consumer/bin/Debug/net9.0/libwidget_bridge.so

# macOS
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --output Consumer/bin/Debug/net9.0/libwidget_bridge.dylib
```

Call the generated C# API:

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

`native-build` verifies real exports by default. For NuGet packaging, use `--package-root artifacts/package` to produce `runtimes/<rid>/native/` plus the SHA-256 asset index.

## Ownership and allocator pattern

1. The allocating module frees the memory. Never free native `new`, `malloc`, or custom-allocator memory with `Marshal.FreeHGlobal`.
2. Every owning handle has one explicit destroy/release function.
3. Document the validity window of every borrowed pointer.
4. Return buffer length and a same-module release function; do not represent arbitrary bytes with a terminator.
5. Never allow a C++ exception to cross the C ABI; return status plus a copyable error channel.

```c
typedef struct bgcs_blob {
    unsigned char* data;
    size_t length;
} bgcs_blob;

API(int) engine_read_blob(void* owner, bgcs_blob* result);
API(void) engine_release_blob(bgcs_blob* value);
```

The release function must use the same allocator family and should clear the carrier to reduce double-release mistakes. Wrap owners in `SafeHandle`; release temporary buffers in `try/finally`. If allocation is known but deallocation is not, reject an owning friendly API.

## Callback lifetime pattern

```c
typedef void (CALL *engine_event_callback)(void* context, int event_code);

API(unsigned long long) engine_register_event(
    void* owner,
    engine_event_callback callback,
    void* context);
API(void) engine_unregister_event(void* owner, unsigned long long token);
```

Return a token instead of using the callback address as identity. No new callback may begin after `unregister` returns. If callbacks run concurrently, unregister must wait for in-flight callbacks or expose a separate drain API. Native code borrows `context`; managed code roots the delegate and state through the unregister barrier. Test callback thread, reentrancy, exception handling, and dispose races. Catch managed exceptions before they leave the unmanaged frame.

Without an unregister barrier, a safe wrapper needs shared reference-counted registration state. Freeing the delegate immediately in `Dispose` is a use-after-free risk.

## Async completion pattern

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

Define whether completion occurs exactly once or follows an explicit progress/terminal state machine. State whether cancel is a request or a completion barrier; normally cancellation still waits for a terminal completion. Define payload lifetime. Release the operation only after terminal completion/drain. Managed code should coordinate `TaskCompletionSource`, a rooted callback, cancellation registration, completion, cancel, and dispose atomically. Shutdown must drain/join before unloading the native library.

## Correct use of `AllowUnsafe`

| Policy | Accepted evidence | Intended use |
| --- | --- | --- |
| `VerifiedOnly` | BGCS verified built-ins | Default and unknown third-party input |
| `AllowUserAsserted` | Reviewed project recipes/plugins/shims | Normal project extension |
| `AllowUnsafe` | Explicit unsafe extensions with `BGCS-SAFETY-LOWERING-BYPASS` | Temporary migration or externally owned audit risk |

An unsafe path still needs native compilation, export inspection, real invocation, error-path, allocator, dispose-race, and deterministic-regeneration tests. Do not set the whole project to unsafe merely to make generation continue.

## Completion checklist

- [ ] Nothing under `Generated/` is edited; clean regeneration is stable.
- [ ] Project semantics stay in recipe/plugin/shim, not library-specific BGCS core branches.
- [ ] Every owner has one release path; every borrow has a validity window.
- [ ] Callbacks have unregister/drain; async operations have terminal/drain.
- [ ] Exceptions do not cross C ABI and the error channel is tested.
- [ ] The native library passes export inspection and real invocation.
- [ ] Plugin/shim inputs participate in cache fingerprints.
- [ ] Every claimed platform has an independent host report.
