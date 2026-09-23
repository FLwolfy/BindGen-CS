# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

BindGen-CS 是一个面向生产环境的跨平台 C/C++ → C# Binding 工具链：C ABI 可以直接生成 C# interop；C++ class、模板实例和选定 STL 类型可以先生成 ABI 稳定的 C Bridge，再自动生成对应 C# bindings。

> **当前可信状态：** 当前源码已经产出完整通过的 `macos-arm64-darwin` 报告，十个验收分类全部达到 9.0/10。其余平台都是 BindGen-CS 的正式支持目标，但在各自实现、打包和当前版本实机报告全部闭环之前统一标为 ⚠️，不会用其他平台的结果代替。BindGen-CS 对无法证明安全的语义给出诊断，不猜测 ownership、allocator 或 C++ ABI。

## 产品宗旨、目标与边界

BindGen-CS 的宗旨是：**让常见 C/C++ → C# binding 默认自动、复杂语义显式可扩展、所有结果可重复构建并可验证，而不是靠手改生成代码维持。**

| 目标 | BGCS 的做法 |
| --- | --- |
| 一分钟开始 | 从 header 生成配置；一条命令生成，一条命令编译检查 |
| 安全默认值 | ABI、ownership、allocator、buffer 和 callback lifetime 证据不足时 fail-closed |
| C++ 可用性 | 为 C++ class/template/STL 生成稳定 C ABI bridge，再生成 C# surface |
| 项目可扩展 | recipe、typed lowering plugin 和项目拥有的 C shim，不向 core 加库名特判 |
| 工程可维护 | 事务性输出、确定性 diff、workspace、增量缓存、target report 和发布 gate |

明确限制：BGCS 不是“把任意 C++ 源码逐行翻译成 C#”的编译器。宏元编程、不可访问的 private 行为、未实例化模板和无法建立稳定 ABI/lifetime 契约的能力不能凭空绑定；但只要能力能暴露为稳定可调用的 C ABI，通常都可以通过配置、plugin 或 C shim 接入。`AllowUnsafe` 只接管已知风险，不能修复无效 ABI。生成目录始终是可重建产物，不应手工编辑。

## 支持状态总览

图例：完整验证 ✅　实现或实机验收待完成 ⚠️。所有列出的平台都是支持目标；升级为 ✅ 要求代码、打包、测试与当前版本实机报告同时成立。

| 能力 / target | 状态 | 边界与证据 |
| --- | :---: | --- |
| 默认 IR-native C#（raw + string/span/ref/out friendly surface） | ✅ | 唯一 C# emission 路径；预发布旧 backend 与旧配置迁移已删除 |
| multi-RID native package layout | ✅ | win/linux/osx x64/arm64 的 `runtimes/<rid>/native/` 与 clean consumer invocation |
| SBOM、provenance、API/license/vulnerability gates | ✅ | 确定性 SPDX/SLSA payload 与发布 gate；见[预发布政策](docs/compatibility-policy.cn.md) |
| OIDC/Sigstore 正式发布签名执行 | ⚠️ | release workflow 已实现并申请 GitHub OIDC；真实签名只能由获授权的 GitHub release run 生成，本地报告不冒充该证据 |
| `map/set/array/variant/expected/path/chrono` lowering | ✅ | 已编译并调用真实 native bridge；未知 specialization fail-closed |
| 最终 C++ 扩展架构 | ✅ | 内置 lowering、声明式 recipe、typed lowering plugin、显式 C shim、managed/native artifact 与可审计安全 bypass 共用一个 registry |
| 复杂继承/模板特化与 lifetime contract | ✅ | native 指针调整/模板测试，以及 allocator/callback/async model 与 race test |

## 平台支持与验收

| 平台 / 架构 | 状态 | 当前证据与升级条件 |
| --- | :---: | --- |
| macOS arm64 | ✅ | `macos-arm64-darwin` 完整报告通过；十个强制分类均为 9.0/10 |
| Windows x64 | ⚠️ | runner 与 clang-cl/MSBuild/DLL 测试已配置；需产出同版本完整实机报告和 NuGet native consumer 结果 |
| Linux x64 | ⚠️ | runner 已配置；需产出同版本完整实机报告和 NuGet native consumer 结果 |
| macOS x64 | ⚠️ | Intel runner 已配置；需产出同版本完整实机报告和 NuGet native consumer 结果 |
| Windows arm64 | ⚠️ | target 与 desktop RID model 已有；provider、native invocation 和完整报告待验证 |
| Linux arm64 | ⚠️ | target 与 desktop RID model 已有；需要当前版本独立完整报告 |
| Android（arm/arm64/x64） | ⚠️ | 属于正式支持目标；target model 已有，NDK/sysroot、包布局、设备/模拟器 runtime 验收待完成 |
| iOS（device/simulator） | ⚠️ | 属于正式支持目标；target model 已有，Xcode SDK、framework/XCFramework 布局与实机/模拟器验收待完成 |
| FreeBSD（x64/arm64） | ⚠️ | 属于正式支持目标；target model 已有，toolchain、包布局与独立 runtime 报告待完成 |

## 选择正确的工作流

| 你的输入 | 推荐入口 | 结果 |
| --- | --- | --- |
| C header / C ABI | `bindgen-cs init native.h` | C# imports、类型、常量和可选友好 overload |
| C++ class / template / STL | `bindgen-cs init library.hpp` | `bridge.json`、C Bridge 源码及可选 C# bindings |
| 多个 native library | `bindgen-cs workspace ...` | 一份 workspace 统一验证、生成和 diff |
| 在构建工具中嵌入 | `BGCS` NuGet 包 | 稳定 facade、结构化结果和共享 Binding IR |
| 只消费生成代码 | `BGCS.Runtime` NuGet 包 | 指针、callback、native context 和 ABI runtime 类型 |

更完整的支持范围、证据等级和明确边界见[能力矩阵](docs/capabilities.cn.md)。

## 1 minute：生成第一个 Binding

已有一个 C header 时，只需要 .NET SDK 9.0 和宿主平台上的 Clang/GNU compiler driver 或 Windows LLVM：

```bash
dotnet tool install --global BindGen-CS
bindgen-cs init path/to/native.h
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
```

完成后得到 `Generated/Bindings.cs`。`build` 会在临时消费项目中以 nullable 和 warning-as-error 编译生成源码；它验证 managed bindings，不替代上游 native library 的构建。首次接入建议再运行 `bindgen-cs doctor` 和 `bindgen-cs validate bindgen.json` 查看 toolchain 与安全诊断。

C++ header 使用同样入口；扩展名会让 `init` 创建 `bridge.json`：

```bash
bindgen-cs init path/to/library.hpp
bindgen-cs bridge bridge.json
bindgen-cs native-build GeneratedBridge/bridge.manifest.json
```

完整目录、C/C++ 示例、NuGet consumer 和故障排查见[快速开始](docs/getting-started.cn.md)。

`init native.h` 会生成可立即运行、可提交的配置。Header 和 include 路径相对于配置文件并统一使用 `/`，因此同一配置可以在 Windows、macOS 和 Linux 使用：

```json
{
  "ConfigVersion": 1,
  "Preset": "host-c,c-library",
  "Namespace": "MyCompany.Native",
  "ApiName": "NativeApi",
  "LibName": "native",
  "EntryFiles": ["include/native.h"],
  "IncludeFolders": ["include"],
  "OutputPath": "Generated",
  "ImportType": "DllImport"
}
```

生成目录应当被视为可重建产物：定制 naming、type、function、marshalling 和 ownership 时修改配置，不直接修改生成文件。

## C++ Bridge

```bash
bindgen-cs init include/library.hpp
bindgen-cs bridge bridge.json
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --package-root package
```

这会生成 C ABI wrapper；默认 `init` 配置还会从 bridge header 生成 C# bindings。`bridge.manifest.json` 会记录 generated/original sources、include directories、defines、compiler/linker arguments、language standard、libraries 和 resolved target。`native-build --provider auto|clang|clang-cl|cmake|meson|msbuild` 会将它转换为不经过 shell 的单步或多步构建流水线。构建成功后默认通过 `nm` / `dumpbin` 将所有生成的 `API(...)` 声明与真实二进制导出表逐项核对；`--package-root` 随后写入标准 multi-RID `runtimes/<rid>/native/` 目录及带 SHA-256 的资产索引。只有外部发布 gate 已承担 export 检查时才使用 `--no-verify-exports`。

已验证的 C++ 范围包括 class 构造/析构、instance/static method、overload、namespace function、异常边界、multiple-inheritance pointer adjustment、完整/部分模板特化、显式模板实例、`string/vector/span/array/map/set/optional/variant/expected/filesystem::path/chrono`、smart pointer 和配置式 pure-virtual callback proxy。复杂项目语义可以通过声明式 lowering recipe、typed lowering plugin 或显式 C ABI shim 接入。未证明的规则默认拒绝；`LoweringSafetyPolicy=AllowUnsafe` 是会产生诊断的显式风险接管，不是静默猜测。详见[最终 lowering 架构](docs/lowering.cn.md)。

## CLI

| 命令 | 用途 |
| --- | --- |
| `init` | 从 C/C++ header 或配置路径创建起始配置 |
| `doctor` | 检查 host target、compiler、system includes 和 macOS SDK |
| `validate` | 解析并分析，不写正式输出 |
| `inspect` | 输出分析后的 module 摘要或 JSON |
| `generate` | 事务性生成 bindings |
| `build` | 生成并编译验证 C# 输出 |
| `diff` | 在临时目录重生成并检查 checked-in bindings 是否最新 |
| `workspace` | 批量 `validate`、`generate` 或 `diff` 多个项目 |
| `schema` | 从当前安装版本生成严格的 C 或 C++ JSON Schema |
| `explain` | 以文本或 JSON 列举、解释稳定诊断代码 |
| `bridge` | 生成配置驱动的 C++ → C Bridge |
| `native-build` | 从 bridge manifest 编译 target shared library，或用 `--dry-run` 检查计划 |
| `supply-chain` | 为包/原生产物生成 SPDX 2.3 SBOM 与 SLSA v1 provenance |

完整示例见[快速开始](docs/getting-started.cn.md)，配置决策见[配置指南](docs/configuration-guide.cn.md)。

## BGCS 能力全景

| 分类 | 已实现能力 |
| --- | --- |
| 输入与解析 | C/C++ header、umbrella/transitive header、宏与常量、target system include、LibClang 解析、显式模板实例 |
| C ABI | function、enum、typedef、opaque handle、struct/union、packing、bitfield、fixed array、function pointer 与 callback |
| C++ Bridge | class 生命周期、instance/static/overload/namespace function、异常边界、多继承 pointer adjustment、模板特化、常见 STL、smart pointer、callback proxy |
| C# 输出 | `DllImport`、`LibraryImport`、function table/native context；raw ABI 与 string/span/ref/out friendly API 共用 IR-native emitter |
| Marshalling | string encoding、ownership、cleanup、pointer/count、capacity/written-count、caller allocation、external managed ABI carrier |
| Target/toolchain | platform、architecture、ABI、target triple、sysroot、compiler discovery；Clang/GNU、clang-cl、CMake、Meson、MSBuild provider |
| 构建与打包 | native build manifest、真实 export inspection、multi-RID `runtimes/<rid>/native/`、clean NuGet consumer |
| 配置与规模化 | preset、BaseConfig、strict schema、SingleFile、workspace batch、target-specific output、10,000 declaration 性能 gate |
| 可重复性 | staging transaction、确定性输出与 diff、内容寻址增量缓存、并发 writer 隔离、输入/toolchain/plugin fingerprint |
| 可扩展性 | type/callable recipe、typed lowering plugin、native/managed artifact contributor、显式项目 C shim |
| 诊断与安全 | typed diagnostics、ABI/layout/ownership/lifetime 分析、fail-closed policy、可审计 `AllowUnsafe` bypass |
| 发布治理 | public API gate、license/vulnerability gate、SPDX SBOM、SLSA provenance、OIDC attestation workflow |
| 嵌入 | CLI、`BGCS` facade、`BGCS.Cpp2C`、`BGCS.Runtime`、零依赖 `BGCS.Intermediate` IR |

逐项证据、已验证语义和未验收 target 见[能力与边界](docs/capabilities.cn.md)。

## 高级扩展

- [C++ 扩展实战手册](docs/cpp-extension-cookbook.cn.md)：完整 shim 项目、独立 plugin `.csproj`、`CallableLowerings`、决策树，以及 ownership/allocator/callback/async 推荐契约。
- [最终 lowering 架构](docs/lowering.cn.md)：extension contract 与安全策略。
- [配置指南](docs/configuration-guide.cn.md)：target、mapping、marshalling 和 policy。
- [诊断指南](docs/diagnostics.cn.md)：从诊断代码定位最小修复路径。
- [架构说明](docs/architecture.cn.md)：IR-native pipeline、分层和依赖边界。

## 安全契约

BindGen-CS 将 native 声明分成三类：

1. ABI 和 lifetime 足够明确：自动生成并编译验证。
2. 声明缺少 ownership、allocator、length 或 callback lifetime：给出 `BGCS-SAFETY-*` 诊断和最小配置路径。
3. C++ 类型没有被接受的 lowering：添加 recipe/plugin/shim，以 `BGCSCPP001` / `BGCSCPP-INSTANTIATION` 拒绝，或在 `AllowUnsafe` 下明确继续并保留 `BGCS-SAFETY-LOWERING-BYPASS` 审计诊断。

这条边界是正确性设计，不是功能缺失的静默掩盖。诊断处理方式见[诊断指南](docs/diagnostics.cn.md)。

## InnoEngine 集成

InnoEngine 已为 miniaudio、SDL3、cimgui、cimguizmo 与 bgfx 建立五份 BindGen-CS 配置。一份 workspace 可以 clean regeneration 全部 target-scoped 输出；验收 gate 会拒绝 `Generated/` 外的手写 import、构建全部锁定 native dependency、构建完整 engine solution，并运行全部六个 native-binding test project。当前 macOS Arm64 已完整通过该 gate。集成逻辑只存在于 InnoEngine 仓库，BindGen-CS 核心没有 InnoEngine library-name/path 特判。尚未完成的 desktop-x64 报告仍是独立的 BGCS 发布门槛。

## 架构与嵌入

```text
CLI / Embedded Facade
        ↓
Configuration → Parsing → Analysis → Binding IR
                                      ↓
                  C# / Runtime / C Bridge Emitters
                                      ↓
                         Transactional Output
```

稳定入口：

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
if (!generator.GenerateConfigured())
{
    foreach (var diagnostic in generator.Messages)
        Console.Error.WriteLine(diagnostic);
}
```

需要结构化 IR 时使用 `BGCS.Facade.BindingGenerator`。IR-native 路径与分层职责见[架构说明](docs/architecture.cn.md)。

## 验收

```bash
./scripts/run-full-test-matrix.sh
```

报告只有在 managed tests、原生 ABI/runtime gate、高级 C++/lifetime 语义、真实 C/C++ 库、API 与确定性 snapshot、clean NuGet/tool/native-RID consumer、license/vulnerability policy 和 10,000 declaration 冷/热性能预算全部通过后才生成：

- `artifacts/acceptance/report.json`
- `artifacts/acceptance/report.md`
- `artifacts/acceptance/reports/<target>/report.{json,md}`

评分规则和 target 隔离原则见[验收规范](docs/acceptance.cn.md)。当前验收分数证明声明范围内的质量，不代表完整 C++ 语言覆盖。

## 包

- `BindGen-CS`：提供 `bindgen-cs` 命令的 .NET Tool。
- `BGCS`：可嵌入的 C/C++ → C# facade。
- `BGCS.Cpp2C`：C++ → C Bridge。
- `BGCS.Runtime`：生成 bindings 使用的 runtime。
- `BGCS.Intermediate`：零依赖 Binding IR 和 diagnostics contract。

选择说明见[NuGet 包与公开 API](docs/packages.cn.md)。

## 文档

- [中文文档入口](docs/README.cn.md)
- [快速开始](docs/getting-started.cn.md)
- [能力与边界](docs/capabilities.cn.md)
- [C++ 扩展实战手册](docs/cpp-extension-cookbook.cn.md)
- [配置指南](docs/configuration-guide.cn.md)
- [诊断指南](docs/diagnostics.cn.md)
- [架构说明](docs/architecture.cn.md)
- [超级通用执行路线图](docs/roadmap.cn.md)
- [工程成熟度审计](docs/assessment.cn.md)
- [验收规范](docs/acceptance.cn.md)
- [测试说明](docs/testing.md)
- [发布与 OIDC](docs/publish.cn.md)

## License

BindGen-CS 使用 MIT License，详见 [LICENSE](LICENSE)。从 CppAst/HexaGen 派生的部分保留其原始版权声明。
