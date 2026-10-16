# MSBuild 集成

[English](msbuild.md) | [文档](README.cn.md)

`BGCS.MSBuild` 是随完整 `BGCS` SDK 交付的独立实现工程，决定何时启动公开 Host 流水线；Clang 解析在另外的 worker 进程执行。原生 MSBuild provider 则将冻结的原生构建计划转换成自己生成的工程，不读取任意现有 `.vcxproj`。

生成需要显式启用：

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

没有设置 `BGCSGenerateBindings=true` 时，SDK 部署解析器执行资源，但不生成绑定。DesignTimeBuild 与跨框架 outer build 跳过生成。每个 inner build 使用独立的框架、RID 和原生目标中间目录：

```text
obj/<Configuration>/<TargetFramework>/bgcs/<RuntimeIdentifier>/<BGCSNativeTarget>/
```

集成不依赖应用默认源码 glob。生成后，`ReadArtifactManifestTask` 按 Host 发布锁协议读取完整的已拥有产物集合，验证每个文件 hash，并提供确切的 `Compile` items。

## 执行与取消

Task 使用 .NET 10 的 `Runtime="NET"` 和 `TaskHostFactory`。`GenerateBindingsTask` 实现 `ICancelableTask`，把取消转发给 Host，并保留原始 header 的文件、行号、列号诊断。TaskHost 具有完整托管依赖闭包；解析器资源按生成器宿主选择，不按原生目标选择。

CLI、嵌入式 SDK 与 MSBuild 使用相同的分组配置、插件 manifest 和 Host 流水线。多目标配置必须在每个 inner build 中通过 `BGCSNativeTarget` 选择一个已配置目标。

## 原生产物

`nativeBuild.mode` 声明 source 或 prebuilt 输入时，`BGCSBuildNative` 默认 `true`，通过 `BindingHost.BuildAsync` 验证并暂存这些输入。设置为 `false` 则只请求生成。Host 提供包含已解析目标事实、输出形式和确切路径/hash 的类型化引用描述。每项引用先与完整的已验证发布集合核对，再提供：

- 受支持浏览器 Wasm object/archive 的 `NativeFileReference`；
- 受支持 Apple 移动 archive/object 的 `NativeReference`，带 `Kind=Static` 和 `ForceLoad=true`；
- 桌面静态链接 archive/object 的 `NativeLibrary`，要求消费工程启用 `PublishAot=true`；
- 动态库（包括带版本的共享库文件名）复制到消费者与发布输出目录。

引用选择使用描述中的 kind。自定义目标的不透明 ID 不需要模仿内置平台名称，也不根据文件扩展名猜测消费协议。

浏览器引用还将确切的 `importModuleName` 放入 `BGCSPInvokeModule` metadata。Task 验证该身份与声明的原生文件名一致，并保留文件名：`libapi.a` 注册为 `libapi`，`api.o` 注册为 `api`。分组 target settings 应明确固定消费者选定的 .NET 工作负载 SDK 的编译器 / sysroot，不能用全局 LLVM 代替该 SDK。具体字段见[原生构建指南](native-build.cn.md)。

这些 item 映射是集成机制。实际平台支持由认证收据决定，存在映射不等于已经通过链接与运行调用验收。

## 增量构建与验证

每次请求生成时，Host 检查源码依赖和扩展输入。字节内容未变化的产物保留时间戳，使 `CoreCompile` 可以保持 up to date；新增 header、改变依赖或插件必须使相关生成结果失效。不能通过只比较 MSBuild 输入时间戳绕过 include 目录成员关系检查。

生成中间文件是消费者构建输入。消费者 `CoreCompile`、原生应用链接与实际调用分别建立自己的验证阶段。Task 的生成或构建结果不认证尚未执行的消费者编译或原生应用调用。

CLI/SDK `build` 继续在候选验证成功后才发布。临时托管编译验证不递归启用 BGCS 生成。

`dotnet clean` 在各自发布锁保护下清理完整且已验证的生成集合和已部署 worker 集合。清理限定在捕获的 inner-build 中间目录和应用输出目录内，保护源码 / 包输入，并保留少量有效的空 ownership marker。错误 owner、被修改的产物和未登记文件会被拒绝，不会留下被部分删除的 manifest 集合。重新构建通过相同发布协议恢复两个集合。

.NET TaskHost 验收测试包含显式启用、默认关闭、设计期跳过、精确源码 / 原生 manifest 输入、嵌套托管编译、重复构建时间戳、header 改变后重新生成以及 build/clean/rebuild。Visual Studio、额外项目图和平台认证应记录实际工具版本。
