# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

BindGen-CS 是一个面向生产环境的跨平台 C/C++ → C# Binding 工具链：C ABI 可以直接生成 C# interop；C++ class、模板实例和选定 STL 类型可以先生成 ABI 稳定的 C Bridge，再自动生成对应 C# bindings。

> **当前可信状态：** `macos-arm64-darwin` 与 `linux-arm64-gnu` 完整验收矩阵已分别通过，每个 target 的十个分类均为 9.0/10.0。Windows、x64 宿主、Android、iOS 和 FreeBSD 已进入 target/ABI model，但没有对应 target 的验收报告时，不把设计支持写成实机通过。BindGen-CS 对无法证明安全的语义给出诊断，不猜测 ownership、allocator 或 C++ ABI。

## 选择正确的工作流

| 你的输入 | 推荐入口 | 结果 |
| --- | --- | --- |
| C header / C ABI | `bindgen-cs init native.h` | C# imports、类型、常量和可选友好 overload |
| C++ class / template / STL | `bindgen-cs init library.hpp` | `bridge.json`、C Bridge 源码及可选 C# bindings |
| 多个 native library | `bindgen-cs workspace ...` | 一份 workspace 统一验证、生成和 diff |
| 在构建工具中嵌入 | `BGCS` NuGet 包 | 稳定 facade、结构化结果和共享 Binding IR |
| 只消费生成代码 | `BGCS.Runtime` NuGet 包 | 指针、callback、native context 和 ABI runtime 类型 |

更完整的支持范围、证据等级和明确边界见[能力矩阵](docs/capabilities.cn.md)。

## 五分钟生成第一个 C Binding

要求：.NET SDK 9.0，以及宿主平台上的 Clang/GNU compiler driver 或 Windows LLVM。

```bash
dotnet tool install --global BindGen-CS

# native.h 必须已经存在
bindgen-cs init native.h
bindgen-cs doctor
bindgen-cs validate bindgen.json
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
```

默认输出为 `Generated/Bindings.cs`。`build` 会在临时消费项目中以 nullable 和 warning-as-error 编译生成源码；它验证 managed bindings，不替代上游 native library 的构建。

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
bindgen-cs native-build GeneratedBridge/bridge.manifest.json
```

这会生成 C ABI wrapper；默认 `init` 配置还会从 bridge header 生成 C# bindings。`bridge.manifest.json` 会记录 generated/original sources、include directories、defines、compiler/linker arguments、language standard、libraries 和 resolved target。`native-build --provider auto|clang|clang-cl|cmake|meson|msbuild` 会将它转换为不经过 shell 的单步或多步构建流水线。构建成功后默认通过 `nm` / `dumpbin` 将所有生成的 `API(...)` 声明与真实二进制导出表逐项核对；只有外部发布 gate 已承担此检查时才使用 `--no-verify-exports`。

已验证的 C++ 范围包括 class 构造/析构、instance/static method、overload、namespace function、异常边界、multiple-inheritance pointer adjustment、显式模板实例、`std::string`、`std::vector`、`std::span`、blittable/non-blittable `std::optional`、`std::unique_ptr`、`std::shared_ptr` 和配置式 pure-virtual callback proxy。它不是任意 C++ 语义的自动翻译器；未知 specialization 会明确失败。

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

完整示例见[快速开始](docs/getting-started.cn.md)，配置决策见[配置指南](docs/configuration-guide.cn.md)。

## 核心能力

- 显式建模 platform、architecture、ABI、target triple、sysroot 和 compiler discovery。
- 支持 `DllImport`、`LibraryImport` 和显式 function table/native context。
- 处理 struct、union、packing、bitfield、fixed array、typedef、opaque handle、callback 和 target-dependent primitive。
- 使用 `MarshallingMappings` 表达 string encoding、ownership、cleanup、pointer/count、capacity/written-count 和 caller allocation。
- Pipeline 会生成共享 Binding IR 并据此执行安全分析；`CSharpEmissionBackend=IntermediateRepresentation` 提供配置驱动、fail-closed 的 IR 路径，覆盖 constant、delegate、alias、handle、匿名/嵌套 record、bitfield 和全部 import mode。五个真实 C 库的 raw ABI 输出均通过独立生成与 warning-as-error 编译；只有大小/对齐而缺少字段定义的 opaque storage 若按值跨 ABI 传递会被拒绝。默认兼容 surface 继续隔离，直到 friendly overload 与全部 public-API snapshot 语义等价。输出通过 staging transaction 原子替换。
- 支持 BaseConfig、可组合 preset、SingleFile、workspace、确定性 diff 和 target-specific snapshot。
- 内容寻址增量缓存会精确 hash 输入、compiler/toolchain、配置、plugin 与 adapter fingerprint，原子发布/恢复并隔离 target；已有并发 writer 验收。无法稳定 fingerprint 的有状态自定义扩展会保守地关闭缓存命中。
- 提供版本化第三方 plugin contract、隔离 dependency resolution、原子且确定性的 typed service registry 和 v1 API shape 锁定测试；C++ type/callable adapter 不需要修改核心分支。
- 发布 CLI、generator、C++ Bridge、Runtime 和零依赖 IR 包。

## 安全契约

BindGen-CS 将 native 声明分成三类：

1. ABI 和 lifetime 足够明确：自动生成并编译验证。
2. 声明缺少 ownership、allocator、length 或 callback lifetime：给出 `BGCS-SAFETY-*` 诊断和最小配置路径。
3. C++ 类型无法安全 lowering：以 `BGCSCPP001` / `BGCSCPP-INSTANTIATION` 拒绝生成。

这条边界是正确性设计，不是功能缺失的静默掩盖。诊断处理方式见[诊断指南](docs/diagnostics.cn.md)。

## InnoEngine 实战证明

同仓的 InnoEngine 集成不是演示用 toy header。完整 gate 会：

- 从配置确定性重生成 cimgui、cimguizmo、miniaudio、SDL3 和 bgfx；
- 把 C# 输出隔离到 `Generated/<target>/`，并只编译当前 target，杜绝跨 ABI 产物误用；
- 拒绝 `Generated/` 之外的手写 native import；
- 从锁定源码构建全部 native dependency；
- 以 warning-as-error 构建完整 InnoEngine solution；
- 运行所有 native binding 测试项目。

真实库 gate 会对 miniaudio、SDL3、cimgui、cimguizmo、bgfx C99 的 compatibility 输出执行生成、编译和 target-specific API snapshot，并独立用 IR-native backend 重生成五个库、以 warning-as-error 编译。bimg C++ Bridge 使用独立的 native/rebound/snapshot gate。

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

兼容入口：

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
if (!generator.GenerateConfigured())
{
    foreach (var diagnostic in generator.Messages)
        Console.Error.WriteLine(diagnostic);
}
```

需要结构化 IR 时使用 `BGCS.Facade.BindingGenerator`。当前 C# compatibility emission 与 IR-native 目标路径的区别、分层职责和迁移完成条件见[架构说明](docs/architecture.cn.md)。

## 验收

```bash
./scripts/run-full-test-matrix.sh
```

报告只有在 managed tests、原生 ABI/runtime gate、真实 C/C++ 库、确定性 snapshot、InnoEngine workspace/native build/test、NuGet/tool smoke 和 10,000 declaration 冷/热性能预算全部通过后才生成：

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
- [配置指南](docs/configuration-guide.cn.md)
- [诊断指南](docs/diagnostics.cn.md)
- [架构说明](docs/architecture.cn.md)
- [超级通用执行路线图](docs/roadmap.cn.md)
- [工程成熟度审计](docs/assessment.cn.md)
- [验收规范](docs/acceptance.cn.md)
- [测试说明](docs/testing.md)

## License

BindGen-CS 使用 MIT License，详见 [LICENSE](LICENSE)。从 CppAst/HexaGen 派生的部分保留其原始版权声明。
