# 原生构建与消费

[文档](README.cn.md) · [English](native-build.md) · [公开 API](api.cn.md) · [MSBuild](msbuild.cn.md)

BGCS 分开处理原生声明理解、C ABI 桥接生成、原生代码构建、产物核对与 .NET 消费。头文件解析或 C# 编译成功，不证明原生 ABI 和调用安全。使用 Clang 解析不要求最终原生代码也用 Clang 编译。

## 选择操作

| 操作 | 原生行为 |
| --- | --- |
| `generate` | 生成绑定和 C++ bridge 输入，不编译上游原生实现。 |
| `bridge` | 只请求原生桥接，不要求托管投影。 |
| `build` | 验证候选，并执行声明的源码构建或预编译检查，再联合发布。 |
| `native-build` | 使用生成的锁定中立 manifest 独立构建原生代码。 |
| 消费者 MSBuild | 将精确类型化原生引用加入实际应用构建。 |

SDK 对应入口为 `GenerateAsync`、`BridgeAsync`、`BuildAsync`、`PlanNativeBuildAsync` 和 `NativeBuildAsync`。私有计划输出与正式发布路径、请求和结果所有权见[公开 API](api.cn.md)。

## 声明源码构建

```json
{
  "schemaVersion": 1,
  "library": { "id": "widget", "nativeName": "widget_bridge" },
  "source": {
    "language": "cpp",
    "languageStandard": "c++23",
    "headers": ["include/widget.hpp"],
    "includeDirectories": ["include"]
  },
  "targets": [{ "id": "host" }],
  "api": { "namespace": "Example.Widget", "name": "WidgetApi" },
  "nativeBuild": {
    "mode": "sources",
    "provider": "auto",
    "sources": ["src/widget.cpp"]
  }
}
```

`build` 包含生成的 bridge 和声明的上游源码。路径基于各声明文档。需要声明真实定义、链接依赖和 SDK；解析 header 不会提供外部库的实现。BGCS 不自动下载编译器或 SDK。

`nativeBuild` 支持 `outputKind`、`buildToolPath`、`auxiliaryToolPath`、`compilerArguments`、`linkerArguments`、`libraries`、`libraryDirectories` 和可选 `packaging`。约束和默认值见[生成配置参考](generated/config-reference.cn.md)。编译器、sysroot 和系统 header 属于 `targets[].settings`，构建工具可执行文件属于构建配置。

解析器专用 worker contributor 在 `build` 和独立 `native-build` 中都保持进程隔离。生成 manifest 保存精确解析器身份、可执行闭包、设置及输入来源。每次有效源码验证重新启动 worker 预检并捕获声明状态，不复用之前解析的 capture，也不在 Host 加载 contributor。缺少激活信息、文件变化或设置不符时，在联合发布前失败。Contributor 提供的事实仍须通过实际原生编译器的独立源码和 ABI 检查。

## Provider 与工具前提

| ID | 所需已安装工具 |
| --- | --- |
| `auto` | 明确且符合目标的编译器/provider。 |
| `clang` | 匹配的 Clang driver，以及需要时的 LLVM archive 工具。 |
| `gnu` | 匹配的 GCC/G++ 和 archive 工具。 |
| `msvc` | MSVC、匹配的 Visual C++ 工具和 Windows SDK。 |
| `clang-cl` | clang-cl、匹配的 Visual C++ linker/工具和 Windows SDK。 |
| `cmake` | CMake 3.24 或更新、Ninja 和已解析原生编译器。 |
| `meson` | Meson 1.0 或更新、Ninja 和编译器；CI 实际门禁固定 Meson 1.8.3。 |
| `msbuild` | 原生 MSBuild、匹配的 Visual C++ 工具和 Windows SDK。 |
| 自定义稳定 ID | 注册的 `INativeBuildProvider` 及完整输入声明。 |

CMake、Meson 和原生 MSBuild provider 根据声明输入生成自己的冻结构建图，不将任意现成 CMake/Meson/vcxproj 工程当作隐式外部构建契约。`BGCS.MSBuild` 在托管项目构建时启动 BGCS；原生 `msbuild` provider 编译原生代码，两者独立。

显式工具路径具有权威性。工具缺失或 driver 不符时失败，不从 PATH 偷换另一套 SDK/ABI。计划采用参数数组、精确子进程环境和统一进程执行器。用户参数及 response/config 输入不能暗中改变已声明目标身份。

直接编译器与原生 MSBuild provider 的 `auxiliaryToolPath` 明确指定 archive 工具；CMake/Meson 则用它指定 Ninja。直接编译器 provider 拒绝 `buildToolPath`，编译器应固定在 target settings。Clang 通过 driver 或 Windows 直接执行强制使用显式 linker；GNU 在 driver 映射尚不能证明时拒绝任意显式 linker pin。知道编译器路径，不等于已经独立固定 driver 隐式选择的 linker。导出检查器的显式路径同样有权威性，包括 Windows `dumpbin`；缺失时不回退另一工具。

Provider 已实现，不代表其全部平台组合已认证。认证需要明确目标和工具版本的真实编译、导出和调用收据，不能只靠命令快照。

GNU 选择核对 driver 实际的有界 `--version` 输出和文件身份，不能只根据 `gcc` 或 `g++` 文件名判断。macOS 通常把 Apple Clang 放在这些别名下；应为 Apple Clang 选择 `clang`，或在 target settings 中固定真正 GCC/G++ 的路径。`BGCS_GNU_CC`、`BGCS_GNU_CXX` 可显式指定 GNU 发现入口；路径缺失或实际家族不符时失败，不偷偷回退。GNU source-view 认证在 Linux 执行，其他受支持 Unix 宿主必须显式选择真正 GNU 工具才执行该项；Clang 与 GNU 收据保持独立。

`doctor --json` 验证完整锁定宿主 worker 闭包，将 `assetDigestsVerified` 与 `parseExecuted = false` 分开。只有三个入口文件、没有匹配 manifest/native 闭包时不能通过。带配置时复用 Host inspection 和实际解析的目标 descriptor，核对 provider 声明的精确工具文件及有界版本探测；archive 工具是否必需取决于 output form。缺失或不兼容工具以明确诊断失败。Doctor 不编译/链接原生代码、不调用库、不下载工具。CLI 无法发现自定义 provider 前提时明确标记待 Host preflight，不当作内置 provider 静默放行。

## 显式目标 triple

`targets[].triple` 可选地指定编译器 triple，SDK 对应 `AnalysisRequest.triple`。它补充目标 ID，不能替换其 CPU、操作系统或 ABI。例如 `android-arm64` 可以指定 `aarch64-linux-android26`；iOS 模拟器应使用其模拟器目标 ID、SDK 以及 `arm64-apple-ios18.0-simulator` 等明确 triple。

内置 provider 在解析前拒绝不兼容的覆盖值。GNU 与 musl、iOS 设备与模拟器、Windows MSVC 与 GNU、wasm32 Emscripten 与 WASI 均保持区分。支持的部署版本信息进入报告、原生计划和输入身份。这个字段不接受带版本的 MSVC 兼容性覆盖；兼容 profile 由实际安装并选定的工具链决定。自定义目标 provider 定义自己的 triple 策略。编译器、SDK/sysroot 和原生二进制仍需独立核对；接受 triple 只代表目标解析，不代表平台已认证。

## 预编译输入与资产

`nativeBuild.mode = "prebuilt"` 通过 `artifacts` 和具体 `prebuiltTargetId` 声明输入。CPU 架构匹配不足以证明 libc、调用 ABI 或编译配置一致；仍需检查产物、目标、导出和输入身份。

`nativeBuild.mode = "none"` 明确省略原生构建，生成可以成功但原生阶段仍未执行。可选资产请求为 `"packaging": { "enabled": true, "packager": "auto" }`。布局使用 descriptor 的平台、CPU 和 ABI，目标 ID 与 NuGet RID 不互换。GNU 与 musl 不能假定共享 `linux-x64`。

资产布局通过不代表干净安装消费或真实调用通过。自定义 packager 通过稳定 ID 注册，并声明完整输入和指纹。

## 输入锁定与验证

生成的 `bridge.manifest.json` 是中立构建 manifest，`bridge.module.json` 是脱离解析器生命周期的类型化 C++ 模型。manifest 保存目标、编译单元、声明、预期导出和必要插件/输入身份。

包含所选源码事实的 manifest 还必须显式保存 frontend 的 `sourceRetainFunctionBodies` 选项，候选构建和独立原生验证都复用精确的提取模式。声明确实是定义（包括隐式或 defaulted 析构）不能证明解析时是否保留了函数体。缺少或为 null 的提取信息会被拒绝；应重新生成 manifest，不能猜测旧文件的解析设置。

源码构建捕获预检和实际编译 header 闭包、有效声明事实、编译器/linker 可执行文件字节、response/config 文件、链接依赖/probe/trace，以及搜索目录成员关系。有效声明比较和原生 trait/layout guard 检查无法仅靠相同文件名或 struct 总大小证明的差异。

发布前再次验证输入闭包和产物。导出和二进制格式核对留下对应执行计划的证据；不支持或不能证明的情况明确失败。原生构建缓存保持关闭，直到完整依赖身份可证明；生成缓存命中不会跳过原生检查。

共享库除 CPU 外还检查 image kind：ELF 要求 `ET_DYN`，Mach-O 要求 dylib/bundle（包括选中的 universal slice），PE 要求 `IMAGE_FILE_DLL`。这些检查仍不证明应用已经真实调用该 ABI。

原生和托管采用相同候选发布策略：必需验证失败保留上一套完整输出。独立 native build 扩展已拥有的生成根时采用联合发布。规划报告最终声明路径，编译仍写私有候选。规划和导出验证都不声称应用已真实调用 ABI。

## 浏览器工具链身份

浏览器 wasm32 原生 object/archive 链接进应用。查询**实际消费项目选择的 .NET 10 SDK**：

```bash
dotnet msbuild Consumer.csproj -nologo -getProperty:EmscriptenSdkToolsPath,EmscriptenCacheSdkCacheDir
```

使用该 SDK 的编译器和配套 sysroot：

```json
{
  "id": "emscripten-wasm32-emscripten",
  "settings": {
    "compilerPath": "/selected/sdk/tools/bin/clang++",
    "sysRoot": "/selected/cache/tools/emscripten/cache/sysroot",
    "systemIncludeDirectories": ["/selected/cache/tools/emscripten/cache/sysroot/include"],
    "cxxSystemIncludeDirectories": ["/selected/cache/tools/emscripten/cache/sysroot/include/c++/v1"]
  }
}
```

Windows driver 是 `clang++.exe`。这些路径是示意值，实际路径由查询返回；不能改用全局无关 LLVM。解析、原生构建和应用链接必须对应同一工具链。workload 是使用前提，不是 BGCS 隐式下载内容。

导入模块身份来自已声明的真实原生依赖：`libapi.a` 对应 `libapi`，`api.o` 对应 `api`，消费者直接提供的 `api.c` 对应 `api`。BGCS 保存类型化 `importModuleName`，MSBuild 验证精确文件名，不重命名资产掩盖差异。

## 类型化消费引用

| Kind | 消费方式 |
| --- | --- |
| `DynamicCopy` | 精确动态库复制条目，包括带版本文件名。 |
| `BrowserNativeFile` | 匹配导入模块身份的 `NativeFileReference`。 |
| `AppleStatic` | `NativeReference`，带 Static/ForceLoad 元数据。 |
| `NativeAotLibrary` | `NativeLibrary`，要求支持的 NativeAOT 消费 profile。 |

类型来自已核对产物形式和运行时/目标能力，不猜文件扩展名或目标 ID 前缀。桌面静态形式采用支持的 `__Internal` adapter，浏览器采用真实 dependency 模块名。见 [MSBuild](msbuild.cn.md)。

真实独立原生、NativeAOT、浏览器和安装包门禁见[测试](testing.cn.md)、[ABI 安全](abi-safety.cn.md)与[包](packages.cn.md)。未认证平台/provider 仍明确待验证，即使目标解析或计划创建成功。

## 所选原生编译器的源码视图

默认 worker frontend 先取得所选 MSVC、Clang 或 GNU 编译器的实际预处理视图，再让 Clang 提取中立事实，使条件编译和 builtin 能力先决定真实声明。即使禁用原生构建，`generate` 仍需要实际安装的编译器及输入真正引用的 SDK 头文件。自包含的跨目标预处理不代表原生链接器或目标 SDK 已经可用。自定义 `IFrontend` 接收已解析请求，负责其自己声明的解析能力。

MSVC 要求 `/Zc:preprocessor` 标准预处理模式以及实际可用的 `/PD` 最终宏状态输出；显式请求传统预处理器时拒绝执行，响应文件中的相同选项也不例外。无法忠实映射到原生编译器的解析参数会明确失败。Clang 和 GNU 分别执行带依赖跟踪的源码及 `-dM` 最终宏操作，保留注释、源码位置、响应文件/配置输入和最终 `#undef` 状态。替换受控输出或删除源码来源的选项明确拒绝，包括响应文件中的同类选项。

源码视图保留原始位置、注释、最终宏值和 SDK 系统头分类；无法唯一确定的宏来源保持未知。未使用的依赖模板 intrinsic 可以保留，Clang 无法证明的具体求值仍然失败。仅在原生构建配置中增加的宏定义独立保留，并在实际构建时与已生成事实核对。

未经源码修改、且与实际命令行定义的名称、形态和替换 token 完全匹配的最终宏，具有明确的命令行来源：以信息诊断记录参数摘要，不虚构头文件位置。依赖文件、preamble、postamble 或未启用分支中可能出现的同名 `#define`、`#undef` 都会阻止这种归属；不明确的来源仍为警告。原生编译器的警告与说明在预处理后仍保存在结构化诊断中，frontend 控制台阈值只影响展示。源码视图子进程为 MSVC 设置 `VSLANG=1033`，为 GNU/Clang 设置 `LC_ALL=C`，请求稳定的诊断格式；仅安装其他语言资源时，MSVC 文本仍可能本地化，严重级别、诊断编号和实际位置独立保留。这些局部环境值与诊断进入观察身份，不改变用户的原生构建环境。

分析返回或生成发布前，BGCS 再次运行预处理，比较完整展开源码和最终宏状态。这能够发现改变源码视图的绝对路径及动态展开 `__has_include` 变化，即使普通 header 依赖清单不能枚举这些查询；它没有声称记录所有文件系统查询。源码视图发生变化时拒绝候选并保留上一套输出。随时间变化的宏等输入可能因此需要显式确定性定义。driver、它实际报告的语言 frontend、已捕获的源码/头文件/参数输入与工具目录成员关系会在操作前后及发布前核对。MSVC 冻结完整工具目录；GNU 风格 driver 冻结所选可执行文件，并在 Windows 冻结相邻 DLL。这不等于认证了所有隐式操作系统依赖。

只有新一轮真实预处理确认源码 token、最终宏、已捕获依赖、编译器身份及实现字节一致，才能复用中立事实与 C# emission。缓存命中仍执行所需的观察重放，以及本次请求的构建和验证。仅实际行指令元数据中的私有 aggregate 文件名可以规范化；字符串、raw string 或展开的 `__BASE_FILE__` 中的路径仍属于语义输入。无法完整描述可复用输入时，受影响缓存继续禁用。平台认证必须执行实际 provider 门禁；Windows Clang/MSVC 的证据不能替代其他操作系统上 GNU 或 Apple 工具链的验证。

## C 头文件辅助函数

C 头文件中定义的 `static` 或 inline 函数不一定在上游动态库中导出。`source.includeHeaderHelpers` 默认为 `false`：普通 static、inline、仅可直接调用的辅助函数及其别名不会变成必需导入，现有库的外部 API 不需要为此重新构建。源码分析仍保留原始事实。

设为 `true` 后，BGCS 为每个选中的头文件辅助函数生成真实 C adapter。公开 C# 名称保持原函数名，ABI manifest 保存实际分配的 wrapper 导出与原始声明身份。

产物为 `native/include/<prefix>_helpers.h`、`native/src/<prefix>_helpers.c`，映射位于 `native/metadata/header-helpers.manifest.json`。C 源码必须和普通 C 导出一起编译到配置指定的同一个原生模块。`nativeBuild.mode = sources` 自动收集这些生成源码；使用外部 CMake 工程时，可以通过 `target_sources` 将 manifest 中的原生 C 源码产物加入对应库 target。

单独加载辅助 DLL 不会自动转发原库的导出。生成 API 使用一个配置模块，应将两组导出放入该模块，或显式提供同时拥有并路由所需模块的 FunctionTable module provider；BGCS 不推断这种路由。精确辅助函数选择和必需 cleanup 在禁用时会指出此开关。显式 `source.variadicVariants` 固定 adapter 不受普通辅助函数开关影响。

即使禁用原生构建，`generate` 在需要辅助函数时也会输出可复用的原生构建 manifest。预编译库必须已经包含精确的 wrapper 导出；否则 `build` 明确指出需要编译的源码和模块，并保留上一套完整输出。参数列表不明确或含可变参数的辅助函数需要显式固定签名的原生 shim。生成 header 会重新交给 frontend 解析，核对 wrapper 参数和返回类型后才能发布。

## 原生字符串常量证明

原生构建按实际编译器的 execution charset 参数独立核对选中的字符串常量。证明输入使用原生
预处理产生的字面量 token，不把 C 头文件改成 C++ 重新解析，也不根据预期托管字节重新构造
字面量。无头文件的 C++ constexpr 调用检查每个 code unit、宽度与完整长度，包括嵌入和末尾
零单元。因此，即使原始头文件是 C17，所选编译器也必须提供需要的 C++ frontend；缺失 frontend
或无法证明的字面量形式会明确失败。

构建收据的 `literalProofs` 在临时输入删除后保留精确命令、完整证明源码及摘要、被核对的常量
ID 和实际完成的编译器结果。独立原生构建的 SDK 结果及目录验证文件保留相同收据。这些具体证明
不会把源码的 `Declared` 或配置的 `UserAsserted` 自动改成泛化的 `Verified` 声明。
