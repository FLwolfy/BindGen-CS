# MSBuild 集成

[English](msbuild.md)

安装 BGCS SDK 后，只有显式设置 `BGCSGenerateBindings=true` 才会生成绑定：

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

Task 在独立 .NET TaskHost 中调用 `BindingHost.GenerateAsync`，按完整且验证过的产物 manifest 添加生成的托管 `Compile` 项，并部署解析 worker。默认关闭；design-time 和 cross-targeting outer build 不生成。每个 TFM / RID / `BGCSNativeTarget` inner build 使用独立的 `obj/.../bgcs` 输出。

该 Task 不构建消费者项目、不编译原生库，也不注入 `NativeReference`、`NativeFileReference`、`NativeLibrary` 或原生复制 / 链接步骤。原生库由使用者构建并通过普通项目项显式部署或链接；应用自身的 CoreCompile 编译生成的 C#。

配置、插件和生成语义与 CLI / SDK 一致。多目标配置用 `BGCSNativeTarget` 选择一个配置目标。取消会传递到 Host 并等待会话停止；候选检查失败保留上一套完整输出。`dotnet clean` 只清理验证过且归该 inner build 所有的托管生成集合和 worker，保护源文件、包与非拥有文件。重建重新部署 worker 并生成源码。

验收保留 opt-in、默认关闭、design-time、精确 Compile 集合、冷暖重建、header 改变与 Clean/rebuild；原生调用在测试自己的外部构建步骤中验证。
