# 能力与边界

[English](capabilities.md) | [中文文档入口](README.cn.md) | [验收规范](acceptance.cn.md)

这份文档回答两个问题：BindGen-CS 现在能可靠完成什么，以及哪些能力仍然需要显式配置或更多 target 证据。它描述当前实现，不是愿景清单。

## 证据等级

| 等级 | 含义 |
| --- | --- |
| 实机验收 | 在声明 target 上经过真实 header、生成代码编译、native invocation 或集成测试 |
| 自动测试 | 有单元/集成/生成代码编译测试，但不代表所有宿主都完成真实库验收 |
| 配置支持 | 具备明确模型和诊断，需要项目提供 native 语义 |
| 明确拒绝 | 无法安全推导时失败，不生成猜测代码 |

## C 与 ABI

| 能力 | 状态 | 证据或边界 |
| --- | --- | --- |
| function、enum、constant、typedef | 实机验收 | 五个真实 C API 的确定性 snapshot 与 IR-native 零警告编译 |
| struct、union、packing、fixed array、bitfield | 实机验收/自动测试 | layout 与 native invocation gate |
| opaque handle、pointer typedef、forward declaration | 实机验收/自动测试 | SDL3、bgfx、cimgui 及生成代码编译矩阵 |
| callback、function pointer、callback registry | 实机验收/自动测试 | C ABI callback 与 Runtime 测试 |
| C variadic | 配置支持 | 必须声明完成 default argument promotion 的固定 variant；当前限 `DllImport` |
| target-dependent `char`、`long`、`wchar_t`、`long double`、`va_list` | 实机验收/自动测试 | MSVC/GNU/Darwin ABI mapping；Darwin/GNU Arm64 报告包含 AAPCS64 `va_list` carrier |
| `DllImport`、`LibraryImport`、FunctionTable | 自动测试 | 配置化 import mode 与 native context 的生成编译/runtime coverage |

## Friendly API 与安全

| 能力 | 状态 | 证据或边界 |
| --- | --- | --- |
| naming、type、field、function mapping | 实机验收/自动测试 | 配置 entry tests 与真实 API snapshot |
| string encoding 与 ownership | 配置支持 | 声明未表达 lifetime 时必须使用 `MarshallingMappings` |
| pointer/count、capacity/written-count、Span | 配置支持/自动测试 | 保守推断加显式 mapping；严格模式可删除高风险 friendly overload |
| cleanup function 与 owned return | 配置支持 | allocator/cleanup 必须来自项目配置 |
| callback lifetime | 配置支持 | 未声明 lifetime 时产生 `BGCS-SAFETY-CALLBACK` |
| 生成 API 稳定性 | 实机验收 | 确定性 source hash 与 reflection public-API snapshot |

## C++ Bridge

| 能力 | 状态 | 证据或边界 |
| --- | --- | --- |
| class、构造、析构、instance/static method | 实机验收/自动测试 | native bridge compile/invocation gate |
| overload、namespace function、异常边界 | 实机验收/自动测试 | 生成 symbol 与 exception channel 测试 |
| 继承 cast 与 pointer adjustment | 自动测试 | 不使用不安全的简单 reinterpret cast |
| class/function template | 配置支持/自动测试 | 只生成 `TemplateInstantiations` / `FunctionTemplateInstantiations` 明确列出的实例 |
| `std::string` | 自动测试 | UTF-8 borrowed/return lowering 的已验证范围 |
| `std::vector`、`std::span` | 自动测试 | pointer/count view；ownership 仍由配置决定 |
| `std::optional<T>` | 自动测试 | blittable presence/value 与 non-blittable owned-handle protocol 均有 native compile test |
| `std::array/map/set` | native invocation test | fixed extent 与 owned-holder protocol 实际编译执行 |
| `std::variant/expected` | native invocation test | alternative/value/error holder 实际创建、读取、销毁 |
| `std::filesystem::path`、`std::chrono` | native invocation test | UTF-8 path 与纳秒 duration/time-point 转换实际执行 |
| `std::unique_ptr`、`std::shared_ptr` | 自动测试 | 已建模 ownership transfer/retention 的支持路径 |
| pure-virtual managed callback proxy | 自动测试 | 必须显式列入 `VirtualCallbackInterfaces` |
| 任意 STL/container/template/metaprogramming | 明确拒绝 | 未知 specialization 产生 `BGCSCPP001`，不会伪装成 blittable 类型 |

## 工作流与工程化

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| `init → doctor → validate → generate → build` | 实机验收 | Tool 安装与干净消费 smoke test 覆盖 |
| transactional output | 自动测试 | 失败不会破坏 last-good output |
| deterministic `diff` | 实机验收 | 真实库与隔离输出 gate |
| multi-project workspace | 实机验收 | workspace validate/generate/diff；InnoEngine 五项目 clean regeneration 与 native/build/test gate 已在 macOS Arm64 通过 |
| C++ native build manifest/providers | native invocation + plan test | direct Clang/GNU 与 CMake 宿主执行；Windows clang-cl/MSBuild tests 在所属 runner build/export/invoke DLL；Meson plan coverage |
| 增量生成 | 自动化 + 性能测试 | SHA-256 input/config/compiler/plugin/lowering/shim fingerprint、原子 immutable entry、并发发布、删除 output 后恢复、10k declaration 冷/热预算 |
| 最终 lowering 扩展 contract | native invocation + 外部 assembly E2E + API-shape 测试 | 隔离 plugin loader、确定性 typed service、声明式 recipe、显式 shim、managed/native artifact、安全 bypass 诊断与稳定 cache fingerprint |
| BaseConfig 与 preset | 自动测试 | 显式 config-directory context、循环检测、override precedence，不修改进程 cwd |
| 当前安装版本 C/C++ schema | 自动测试 | 默认严格 root property；从安装版本生成嵌套 public shape 和核心说明 |
| 可移植项目初始化 | 自动测试 | config-relative `/` path、显式 C/C++ 选择、拒绝覆盖 |
| deterministic NuGet packages | 实机验收 | 双次 pack 内容对比、干净 restore、Tool 安装 |
| IR-native raw ABI backend | 实机验收 | 五个真实 C 库以 warning-as-error 生成编译；unsupported/opaque by-value 语义在提交前失败 |

## Target 证据

状态：完整验证 ✅；正式支持目标、实现或实机验收待完成 ⚠️。

| Target | 状态 | 当前证据 |
| --- | :---: | --- |
| macOS arm64 Darwin | ✅ | 当前源码完整报告已通过；十个强制分类全部达到 9.0/10 |
| Windows x64 MSVC/clang-cl | ⚠️ | 已配置独立 runner 与真实 clang-cl/MSBuild tests；完整报告待产出 |
| Linux x64 GNU/Clang | ⚠️ | 已配置独立 runner；完整报告待产出 |
| macOS x64 Darwin | ⚠️ | 已配置 Intel runner；完整报告待产出 |
| Windows/Linux arm64 | ⚠️ | target 与 desktop RID model 已有；完整 provider/runtime/package 报告待产出 |
| Android | ⚠️ | 正式支持目标；target model 已有，NDK/sysroot、包布局和 device/emulator 报告待完成 |
| iOS | ⚠️ | 正式支持目标；target model 已有，Xcode SDK、framework 布局和 device/simulator 报告待完成 |
| FreeBSD | ⚠️ | 正式支持目标；target model 已有，toolchain、包布局和独立报告待完成 |

## 成熟度判断

BindGen-CS 已经是强大的工程化 binding toolkit，而不是简单的 header-to-`DllImport` 脚本。它在当前已验收 target 上具备生产级 C 绑定、很强的受控 C++ Bridge、可重复生成、包验证和大型项目落地证据。

但“对所有 C++ 自动通吃”和“所有 target 都已生产验证”目前都不成立。主要差距是：

- Windows 与 x64 宿主还缺同等级的完整 target-specific acceptance artifact；
- 配置模型能力很强但仍较扁平，大型库需要理解 mapping 与 safety policy；
- clang-cl/MSBuild 仍需所属 Windows target 的真实执行证据；桌面 multi-RID packaging 已实现，但 Windows runtime 仍未实机证明；
- 任意 metaprogramming、custom allocator 与超出内置协议的类型需要声明式 lowering、版本化 plugin 或显式 C ABI shim。

因此，准确定位是：**在经过验收的 C ABI 与明确支持的 C++ 子集内非常优秀；作为“任意 C++、任意平台、零配置”的万能工具仍有清晰距离。**

## 下一阶段最高价值工作

1. 在 Windows x64、Linux x64 与 macOS x64 运行同级真实库、native invocation 和包验收并生成独立报告。
2. 持续增加 IR-native public-API snapshot；当前不存在预发布 legacy 路径。
3. 在各自 target 上执行全部 provider，并保留 multi-RID consumer 证据。
4. 在每个采用 target 上重复并保持 InnoEngine clean-regeneration/import-audit/native-build/full-solution/native-test gate。
5. 在授权 GitHub release 环境真实执行 OIDC/Sigstore 签名发布。
6. 持续扩展内置语义/schema，并将项目专用语义统一接入最终 lowering 架构。
