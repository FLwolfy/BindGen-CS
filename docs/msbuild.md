# MSBuild integration

[中文](msbuild.cn.md)

Install the BGCS SDK and explicitly opt into generation:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <BGCSGenerateBindings>true</BGCSGenerateBindings>
    <BGCSConfig>bindgen.json</BGCSConfig>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="BGCS" Version="3.0.0" PrivateAssets="all" /></ItemGroup>
</Project>
```

The task runs `BindingHost.GenerateAsync` in a separate .NET TaskHost, adds exact managed `Compile` items from the verified complete publication, and deploys the parser worker. Generation defaults off. Design-time and cross-targeting outer builds skip it. Each TFM / RID / `BGCSNativeTarget` inner build has its own `obj/.../bgcs` output.

The task does not build consumer projects or native libraries. It injects no `NativeReference`, `NativeFileReference`, `NativeLibrary`, native copying or linking. Build and deploy/link your native library through ordinary explicit project items; the application's own CoreCompile compiles generated C#.

CLI, embedded SDK and MSBuild share configuration, plugins and generation semantics. Select one configured target using `BGCSNativeTarget` in multi-target inner builds. Cancellation reaches the Host and waits for session cleanup. Candidate failures preserve the previous complete output. `dotnet clean` clears only verified owned managed-generation and worker sets, protecting source, package and unowned files. Rebuild deploys the worker and generates again.

Acceptance retains opt-in/default-off/design-time behavior, exact Compile ownership, repeated-build timestamps, changed-header regeneration and Clean/rebuild. Native invocation is verified using the tests' separate external build step.
