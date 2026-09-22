# 超级通用 BindGen 执行路线图

[English](roadmap.md) | [能力矩阵](capabilities.cn.md) | [验收规范](acceptance.cn.md)

## 最终定义

“超级通用”不表示对未知 C++ 语义进行猜测。BindGen-CS 的最终标准是：在明确的 target、ABI、ownership 和 build contract 下自动生成正确代码；可扩展的类型通过通用 adapter 接入；无法证明安全的声明给出稳定、可修复的诊断。

最终发布门槛：下列维度全部达到 9.0/10.0，任何单项失败都不能以平均分掩盖。

| 维度 | 9.0 验收条件 |
| --- | --- |
| C ABI 完整性 | 常见 declaration/layout/callback/variadic 模式有 native compile、layout 和 invocation 证据 |
| C++ Bridge | 已声明的 class/template/STL/ownership 子集全部经过 native 调用；未知语义明确拒绝 |
| Managed API 质量 | raw ABI 正确，friendly API 的 ownership、encoding、length、cleanup 可审计 |
| 易用性 | 新项目可通过 `init → doctor → validate → generate → build` 完成；错误包含具体配置修复路径 |
| 跨平台 | 每个宣称生产支持的 target 都有独立 acceptance artifact，不以模型支持代替实机证据 |
| 架构 | parser、analysis、IR、emitter、runtime、tool/build provider 依赖单向且有自动边界测试 |
| 可扩展性 | 新 type adapter、emitter、build provider 不需要修改无关核心层或添加 library-name 特判 |
| 性能与确定性 | 冷/热生成预算、缓存命中、稳定 hash、并发安全均有 gate |
| 发布与供应链 | deterministic package、SBOM/provenance、兼容策略、干净消费者测试全部通过 |
| InnoEngine | native bindings 全部由配置和生成流程拥有，禁止手写 import，完整 native/build/test gate 通过 |

## 不可妥协的设计规则

1. 不在核心代码中识别某个 native library 名称并走特殊分支。
2. 特殊 native 语义只能表达为通用配置、adapter/provider 接口或可复用分析规则。
3. raw ABI 正确性优先于 friendly API 数量；无法证明 lifetime 时保留 raw API 并诊断。
4. target support 必须绑定 compiler、triple、sysroot、ABI 和真实运行/编译证据。
5. 生成目录是完整事务输出；用户扩展放在配置、插件或独立 partial 文件中。
6. 新功能必须同时具备 positive、negative、snapshot/compile 中至少两类测试；ABI 功能必须有 native 测试。

## 执行阶段

### Phase 0：可度量基线

状态：已完成。

- 建立十类 acceptance scoring、真实库矩阵、InnoEngine gate 和 package smoke tests。
- 文档区分实机验收、自动测试、配置支持和明确拒绝。
- 每个 target 生成隔离报告，禁止复用另一个 target 的 9.0 分数。

完成条件：任何能力声明都能链接到测试、artifact 或明确边界。

### Phase 1：零摩擦 CLI 与配置契约

状态：进行中；本轮已完成可移植 `init`、显式语言选择、C/C++ strict schema、配置 contract version 1、带 `explain` 的版本化诊断目录、独立 Tool tests、显式 C++ configuration-directory 解析，以及不修改进程 cwd 的 BaseConfig 循环检测。

- `init` 只写配置相对路径，支持 `.h` 的 `auto/c/cpp` 消歧和自定义 config 位置。
- C 与 C++ schema 分离；root unknown property 默认拒绝，可显式选择兼容模式。
- 把 command parsing/behavior 从 `Program` 拆到按功能分类的 command classes。
- 增加真正的 packaged-tool end-to-end tests：安装本地 nupkg 后运行完整工作流。
- schema 补齐所有 public object 的语义描述、示例、deprecation 和未来 migration 规则。

完成条件：Windows/macOS/Linux 上新用户从 header 到可编译 managed consumer 不需要手改生成配置。

### Phase 2：完整 IR-native 架构迁移

状态：进行中，仍是当前最高架构优先级。`CSharpEmitter` 已严格只消费 IR，`EmitLegacy` 已删除；默认兼容 surface 被隔离到 `AstGenerationStepEmitter`，但在 snapshot 零回退前仍不能删除。

- frontend 只负责解析和 source diagnostics；analysis 产生唯一 canonical `BindingModule`。
- C#、Runtime、C Bridge、inspection 和 future emitters 只消费 IR + emission context。
- 把 legacy `GenerationStep` 能力按 type/function/marshalling emitter 迁入 IR-native pipeline。
- 用 public API snapshot 和 generated source equivalence 保证迁移无功能回退。
- 在源码/public API 等价后移除默认路径中的 `AstGenerationStepEmitter`；legacy API 只保留有时限的 compatibility adapter。
- 扩充 architecture tests，禁止 emitter 重新读取 AST，禁止 Intermediate 引用 parser/runtime/tool。

完成条件：主 C/C++ 配置生成路径不再调用 legacy emission，真实库和 InnoEngine snapshot 无意外变化。

### Phase 3：C ABI 完整化

状态：已具备强基础，需要系统补齐组合覆盖。

- declarator 组合：多级 pointer、function pointer、array/function nesting、匿名类型、flexible array member。
- layout：packed/aligned struct、union、bitfield、zero-width bitfield、target-specific enum/primitive。
- preprocessing：macro constant/expression、conditional declarations、compiler extension 诊断。
- callbacks：cdecl/stdcall/vectorcall、user data、retained/borrowed lifetime、unregister contract。
- variadic：只允许配置声明完成 default promotions 的固定签名变体。
- 每项在 MSVC、GNU、Darwin ABI 下具备 layout 或 invocation gate。

完成条件：真实 C 库失败只能来自明确 unsupported contract，不来自 silent mis-generation。

### Phase 4：C++ 语义 Bridge 与 Adapter SPI

状态：adapter SPI 已完成，更多 C++ 语义仍需扩展。`ICppTypeAdapter` / `ICppCallableAdapter` 已版本化并具备确定性顺序；内置 string/vector/span/optional/smart-pointer 使用同一个 registry，free/static/instance/constructor/destructor callable 使用同一 naming/exclusion SPI。

- 完整覆盖 ctor/dtor、static/instance、cv/ref qualifier、overload、operator、namespace 和异常边界。
- inheritance graph、virtual/non-virtual base、pointer adjustment、RTTI 可用性形成显式模型。
- template 继续坚持 explicit instantiation；增加可诊断的 partial specialization 选择。
- 在增加通用 adapter 时保持 v1 `ICppTypeAdapter` / `ICppCallableAdapter` compatibility baseline。
- 扩展 map/set/array/variant/expected/path/chrono 时，每种 adapter 都必须声明 ABI、ownership 和 invalidation。
- callback proxy 支持 lifetime token、threading policy、exception translation 和 dispose race tests。

完成条件：增加一个新容器 adapter 不修改 parser、核心 emitter 或既有 adapter；未知 specialization 稳定失败。

### Phase 5：Ownership、Marshalling 与 Safety Contract

状态：已存在 safety analyzer 和 mapping，需要形成完整 contract system。

- 统一 borrowed/owned/transferred/shared/pinned/caller-allocated lifetime 模型。
- allocator/deallocator 配对、arena/context、nullable、encoding、length/capacity/written-count 进入 typed contract。
- callback retention、线程调用、同步/异步完成和 unregister 形成可组合策略。
- 生成 SafeHandle/IDisposable/Span/string friendly APIs，同时保留可审计 raw ABI。
- `StrictSafetySeverity=Error` 成为 release/CI 推荐默认值。

完成条件：任何会分配、保留 pointer 或跨调用保存 callback 的 friendly API 都能从 IR 查到完整 lifetime 来源。

### Phase 6：Native Build 与产物编排

状态：核心 provider 已完成；C++ bridge 会生成确定性的 target-specific manifest，内置 direct Clang/GNU、clang-cl、CMake、Meson、MSBuild 流水线，CLI 成功构建后默认用 `nm` / `dumpbin` 核验导出表。

- 在现有 bridge manifest 中增加经过验证的 export inspection 和 provider result。
- Manifest 字段演进时保持 direct、CMake、Meson、clang-cl 与 MSBuild plan 等价；在确有收益时为 CMake/Meson 增加可选 Ninja executor。
- 支持多 RID/architecture 构建、runtime asset layout、library naming 和 loader validation。
- 扩展 `native-build` 的 export verification 和多配置 provider 选择；原库依赖继续通过配置传入。

完成条件：C++ demo 和真实 bimg bridge 可仅靠 config + 标准 provider 生成 native artifact。

### Phase 7：跨平台实机矩阵

状态：macOS arm64 完整；Windows/Linux 仍需同等级报告。

- Tier 1：Windows x64/arm64（MSVC、clang-cl）、Linux x64/arm64（GCC/Clang）、macOS arm64/x64。
- Tier 2：Android arm64/x64、iOS device/simulator、FreeBSD x64；使用显式 sysroot/toolchain。
- 每个 target 运行 managed tests、real libraries、native ABI/runtime、package consumer 和 target snapshots。
- 对不能执行的 cross target 至少 compile/link + artifact inspection，不能记作 runtime pass。

完成条件：README 中每个“生产支持”平台都存在当前版本生成的独立 acceptance report。

### Phase 8：性能、缓存与大规模项目

状态：第一组 release gate 已完成。C/C++ 配置生成使用内容寻址 immutable cache，具备原子恢复/发布和并发 writer 测试；10,000 declaration 冷/热预算进入完整验收。Workspace DAG、内存趋势和共享 parser cache 仍待完成。

- 持续维护 declaration、configuration、compiler/toolchain、plugin 与 adapter 的版本化 cache fingerprint。
- workspace project DAG、并行生成、共享 parser cache 和 isolated output transaction。
- 真实库建立 cold/warm time、peak memory、output size 和 diff stability budgets。
- 10k+ declarations、多个 translation units 和大型模板实例具备压力测试。

完成条件：无变更 workspace 的 warm generation 达到明确预算，缓存绝不跨 target/ABI 污染。

### Phase 9：稳定扩展生态

状态：plugin entry point 与确定性 typed service 的 contract version 1 已实现，包括 C++ type/callable adapter 和附加 IR emitter。隔离 dependency resolution、原子注册、assembly/content fingerprint 与 v1 public-shape 锁定均有测试；formal obsolete window 仍待完成。

- 将 IR、diagnostics、adapter、emitter、build provider 分别定义稳定 public contracts。
- 建立 API compatibility baseline、obsolete window 和 config migration command。
- 提供 Roslyn source-generator/MSBuild task 的薄集成，但核心 generation 保持 host-independent。
- 插件加载具备版本检查、隔离诊断和 deterministic ordering。

完成条件：第三方扩展无需引用内部 parser 实现，minor release 不破坏已发布 contract。

### Phase 10：InnoEngine 全自动迁移

- 为每个 native dependency 建立独立 config；共享规则通过 preset/base config/adapter 组合。
- 禁止 `Generated/` 外手写 `DllImport`、`LibraryImport`、function pointer import 和 native layout mirror。
- workspace 一条命令完成 validate/generate/diff/native build/managed build/native tests。
- 删除旧 binding、重复 runtime 和临时 patch；任何保留 patch 必须是通用、带测试的 transformation。

完成条件：从干净 checkout 删除全部生成目录后，可以完全重建并运行 InnoEngine；BindGen 核心不存在 InnoEngine 名称或路径判断。

### Phase 11：发布与长期维护

- deterministic NuGet/tool packages、SBOM、provenance、license inventory 和 vulnerability gate。
- versioned schema、configuration migration、release notes、兼容性表和最小复现模板。
- 每次发布保留全部 target acceptance artifacts 和性能趋势。

完成条件：发布过程可以从 tag 自动复现，消费者能够判断 config/package/target compatibility。

## 执行顺序

1. 完成 Phase 1，立即降低所有后续测试和采用成本。
2. Phase 2 与 Phase 3 并行演进，但任何新 ABI 功能先进入 IR，禁止继续扩大 legacy path。
3. Phase 4/5 建立 adapter 与 safety contract 后，再扩展新的 STL 类型。
4. Phase 6 完成后扩展 Phase 7 实机平台矩阵。
5. 每完成一条通用能力就迁移一组 InnoEngine binding，持续执行 Phase 10，不等最后一次性切换。
6. Phase 8/9/11 贯穿所有阶段并成为 release gate。

## 每次变更的 Definition of Done

- 没有 native-library-name/path 特判；
- public behavior、negative case 和诊断均有测试；
- ABI/lifetime 功能有 native compile 或 invocation；
- Windows/Linux/macOS 差异使用 target abstraction，不使用 host scattered conditionals；
- 文档、schema、sample 和 package README 同步；
- solution warnings-as-errors build、managed tests、相关 native gate、deterministic diff 全部通过。
