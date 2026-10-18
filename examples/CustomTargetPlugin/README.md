# Contracts-only private target and SDK plugin

This example registers `example-private-host` without adding a built-in platform.
It uses the real host's Clang-compatible ABI under a private platform ID. It is
not a console SDK or a new runtime/aggregate ABI implementation.

First edit `target-sdk.json` to declare the actual target triple, architecture,
ABI, byte order, module capabilities and read-only SDK header paths. The checked-in
profile is x64 Windows/MSVC. For x64 GNU Linux use `abiId: "gnu"` and
`triple: "x86_64-unknown-linux-gnu"`; for Intel macOS use `abiId: "darwin"` and
`triple: "x86_64-apple-darwin"`. Other architectures require their exact facts.
The sample header is intentionally small; a real SDK needs its complete selected
header/definition environment. No host SDK is silently mixed into explicit paths.

From this directory:

```bash
dotnet build BGCS.Example.CustomTargetPlugin.csproj --configuration Release
python write-manifest.py
bindgen-cs generate bindgen.json
```

The project references only `BGCS.Contracts`; it can use that package after release.
The normal SDK entry is `BindingHost.CreateBuilder().AddBuiltIns().Build()` followed
by `GenerateAsync(GenerationRequest.FromFile("bindgen.json"))`. The manifest loads
the same plugin in SDK and CLI. Clang runs in the normal isolated parser worker.
`api.runtime` is explicit because an unknown platform has no inferred managed
runtime. Unsupported runtime/import/aggregate ABI combinations remain rejected.

The provider declares its profile before reading it and fingerprints its bytes.
SDK directories are declared as external inputs as well as being normal parser
inputs. Changing the profile or headers invalidates affected cache entries; late
changes must prevent publication. Executable plugin bytes are separately locked
by `plugin.json`; regenerate it after rebuilding. Factories create session-owned
providers and do not retain a mutable process-global target.

Consumers own native and managed compilation, linking, packaging and deployment.
Generation does not prove a custom runtime or special console can invoke the result.
Existing native module/function-table injection remains available independently.
