# MSBuild integration

[中文](msbuild.cn.md) | [Documentation](README.md)

`BGCS.MSBuild` is an implementation project distributed inside the complete `BGCS` SDK. It decides when to invoke the public Host pipeline; Clang parsing runs in its separate worker. The native MSBuild provider separately translates a frozen native construction plan into its generated project; it does not consume an arbitrary existing `.vcxproj`.

Generation is explicitly enabled:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  <BGCSGenerateBindings>true</BGCSGenerateBindings>
  <BGCSConfig>bindings/bindgen.json</BGCSConfig>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="BGCS" Version="3.0.0" PrivateAssets="all" />
  <PackageReference Include="BGCS.Runtime" Version="3.0.0" />
</ItemGroup>
```

Without `BGCSGenerateBindings=true`, the SDK deploys its parser execution assets and does not generate bindings. Design-time and cross-targeting outer builds skip generation. Each inner build uses its own framework/RID/native-target intermediate root:

```text
obj/<Configuration>/<TargetFramework>/bgcs/<RuntimeIdentifier>/<BGCSNativeTarget>/
```

The integration does not rely on the application's default source glob. After generation, `ReadArtifactManifestTask` reads the complete owned publication under the Host publication lock, validates every artifact hash and supplies exact generated `Compile` items.

## Execution and cancellation

Tasks use `Runtime="NET"` with `TaskHostFactory`, on the .NET 10 baseline. `GenerateBindingsTask` implements `ICancelableTask`, forwards cancellation to the Host and preserves structured header file/line/column diagnostics. The TaskHost has a complete managed dependency closure; parser resources are selected from the generator host, never from the native target.

The same grouped configuration, plugin manifests and Host pipeline serve CLI, embedded SDK and MSBuild. Multi-target configurations must select one configured identity through `BGCSNativeTarget` for each inner build.

## Native assets

When `nativeBuild.mode` declares source or prebuilt inputs, `BGCSBuildNative` defaults to `true` and validates/stages those inputs through `BindingHost.BuildAsync`. Set it to `false` to request generation only. The Host supplies typed reference descriptors containing resolved target facts, output form and exact path/digest. Each reference is checked against the complete verified publication before supplying:

- `NativeFileReference` items for supported browser Wasm static object/archive outputs;
- `NativeReference` items with `Kind=Static` and `ForceLoad=true` for supported Apple mobile archive/object outputs;
- `NativeLibrary` archive/object inputs for desktop static linking, requiring `PublishAot=true` in the consumer;
- dynamic libraries, including versioned shared-library filenames, copied beside the consumer and into its publish output.

Reference selection uses the descriptor kind. An opaque custom target ID does not need to resemble an inbuilt platform name, and filename suffixes are not used to guess the consumption protocol.

Browser references also carry the exact `importModuleName` as `BGCSPInvokeModule` metadata. The task verifies that this identity matches the declared native filename and leaves that filename intact: `libapi.a` registers `libapi`, while `api.o` registers `api`. Pin the compiler/sysroot from the consumer's selected .NET workload SDK in the grouped target settings; a globally installed LLVM driver is not a substitute for that SDK. See the [native construction guide](native-build.md) for the explicit settings.

These item mappings are integration mechanisms. Actual platform support is determined by the platform certification receipts; the existence of an item mapping does not certify linking or invocation.

## Incremental builds and validation

The Host checks source dependencies and extension inputs on each requested generation. Unchanged artifact content preserves its timestamp, allowing `CoreCompile` to remain up to date; a new header, changed dependency or changed plugin must invalidate the applicable generation result. A timestamp-only MSBuild input shortcut must not bypass include-directory membership checks.

Generated intermediates are build inputs. Consumer `CoreCompile`, native application linking and runtime invocation establish their own validation stages. The Task's generation/build result does not certify a consumer compilation or native application invocation that has not executed.

CLI/SDK `build` keeps its candidate-validation-before-publication behavior. Temporary managed validation does not recursively enable BGCS generation.

`dotnet clean` clears complete verified generated and deployed-worker sets under their publication locks. Cleanup stays beneath the captured inner-build intermediate and application output roots, protects source/package inputs and retains a small valid empty ownership marker. It rejects wrong owners, modified artifacts and unlisted data rather than partially deleting a manifest collection. Rebuilding restores both sets through the same publication protocol.

The .NET TaskHost acceptance tests include opt-in, default-off, design-time suppression, exact source/native manifest import, nested managed compilation, repeated-build timestamps, changed-header regeneration and build/clean/rebuild. Visual Studio and additional project-graph/platform certification must be recorded with their actual tool versions.
