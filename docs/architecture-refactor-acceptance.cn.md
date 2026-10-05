# BGCS 架构重构验收

[文档索引](README.cn.md) · [架构](architecture.cn.md) · [完整计划](platform-architecture-refactor-plan.cn.md) · [C# 规范](csharp-development-standard.cn.md)

## 状态和范围

**本机 BGCS 独立验收通过。外部消费方联调另行验收。**

基线为 `3fc481db4eff828fa45f864455ab121a003dd706`，工作区改动尚未提交。
本次结果来自重构后的当前源码；之前的进度记录保存为证据目录中的 `historical-progress.md`。
验收不依赖引擎组件名单、项目资产或游戏行为。

证据根：`artifacts/acceptance/2026-10-04-refactor/`。
完整矩阵的机器报告：`artifacts/acceptance/reports/windows-x64-msvc/report.json`。
要求、源码归属和共同边界对应见 [Plan 逐项核对](architecture-plan-audit.cn.md)。

## 环境

- Windows x64；生成器工程由 `global.json` 选择 .NET SDK 10.0.401。
- 实际互操作消费者使用 .NET SDK 9.0.318、runtime/workload 9.0.20。
- Wasm 原生编译使用所选 .NET workload 提供的 Emscripten 3.1.56，目标为 wasm32。
- 本机 LLVM 和 Visual Studio 2022 Build Tools 用于桌面原生编译。
- 浏览器调用由独立的后台 Edge 进程执行，使用验收专属 profile；消费者和浏览器在结束后释放。

## 终版结果

| Gate | 结果 | 证据 |
| --- | --- | --- |
| Release solution | 零警告、零错误 | `results/full-matrix-plan-audit-final.log` |
| 全部托管测试 | 11 个程序集，1018 项通过，零失败、零跳过 | `results/full-matrix-plan-audit-final.log`、`results/managed-plan-audit-summary.json` |
| 架构依赖和源码归属 | 八个生产项目，零违规 | 全矩阵 architecture gate |
| 手写源码排版 | 零待修改文件 | 全矩阵 style gate |
| 公开 XML | 3226 个可见声明，零缺口 | 全矩阵 documentation gate |
| 真实 C 库 | MiniAudio、SDL3、cimgui、cimguizmo、BGFX 生成、严格编译和快照通过 | 全矩阵 real-libraries gate |
| 真实 C++ 库 | bimg 桥生成、原生语法编译、C# 消费和快照通过 | 全矩阵 real-cpp-libraries gate |
| 公共 API snapshot | 七个库的当前 API 通过 | 全矩阵 api-compatibility gate |
| 独立 NuGet 消费 | 两次 pack 内容一致；干净消费者、CLI、原生资产调用通过 | `packages-terminal/`、全矩阵日志 |
| 性能和缓存 | 10000 声明冷生成 14 秒、缓存生成 5 秒；低于 60/10 秒预算 | 全矩阵 performance gate |
| 依赖审计 | 无已知 NuGet 漏洞；60 项包许可证声明通过 | 全矩阵 supply-chain gate |
| NativeAOT 实际调用 | 三种 import mode，56 项通过 | 下列独立报告 |
| Wasm 解释执行实际调用 | 三种 import mode，56 项通过，实际指针宽度 4 | 下列独立报告 |
| Wasm AOT 实际调用 | 三种 import mode，56 项通过，实际指针宽度 4 | 下列独立报告 |
| 原生错误注入 | NativeAOT 和 Wasm 都在实际 scalar 调用处失败 | 下列负向报告 |
| NativeShim 示例 | 桥和绑定生成、原生构建、消费者实际输出 42 | `results/example-native-shim-*.log` |
| LoweringPlugin 示例 | 独立插件编译，零警告、零错误 | `results/example-lowering-plugin.log` |

独立原生报告：

- `native-aot-publication-final/0c95fa71e66547b999013214c6314350/report.json`
- `wasm-publication-final/267bc28bbc034d43b681db2c09ec5939/report.json`
- `wasm-aot-publication-final/c8daba4c3027462fb25266a7d086b4c8/report.json`
- `native-aot-negative-publication-final/68c3c3dc6aae4ef79d36bc2570da723e/report.json`
- `wasm-negative-publication-final/2bf243e6825c4c789902e1d0451743dd/report.json`

每个正向消费者覆盖 scalar、64 位整数和枚举、native long、bool、布局、混合位域、buffer/capacity、handle、callback、
空 callback、释放、C++ 构造/方法/继承/释放，以及缺失符号、FunctionTable 初始化和 borrowed ownership。
负向测试要求进程失败且 JSON 明确记录真实调用错误；不能用仅编译成功替代运行验收。

## 完成的职责划分

```text
BGCS.Tool          唯一生产 CLI，组合命令与验证
BGCS               C 到 C# 应用流程、配置、分析、Binding IR 和输出
BGCS.Cpp2C         C++ 分析/lowering、Bridge IR、C 桥输出、原生构建
BGCS.CppAst        Clang 解析、AST、目标 provider 和 SDK/builtin headers
BGCS.Intermediate  实际拥有冻结的 Binding/Bridge IR，不依赖其他 BGCS 项目
BGCS.Core          目标契约、插件、缓存、IO、集合、文本和写出基础能力
BGCS.Language      具体语言的词法、语法及 C# 条件编译
BGCS.Runtime       独立的互操作基础库
```

Core 不引用 CppAst；IR 不通过 `Compile Link` 反向借用生成器源码；Cpp2C 不再直接从 AST 输出代码。
参数、类型布局和 ownership 经分析进入冻结 IR。生成器和桥各自只有一条应用编排。
目标 provider 负责 compiler、triple、sysroot、SDK headers 和 ABI；Clang resolver 转换为解析参数。
这使 Emscripten 属于原生目标描述，而非面向某个消费方的绑定分支。

## API 与删除清单

- 公开配置、属性和参数按 camelCase 对齐；命名空间按当前目录职责整理，消费者同步修改。
- 拆分插件契约、语言模型、IR 和构建 DTO；缩小可变模型的公开表面。
- 增加目标 provider、冻结 Bridge IR emitter，以及 CLI architecture/style/documentation 验证入口。
- Runtime 增加三个有界 Span 位域 API，表达实际字节布局和借用所有权；无分配并保留邻近位。
- 删除 Core 到 AST 的依赖、IR 的源码链接、`EmitAst` 路径、空 parser/预处理入口、空缓存 DTO、
  重复目录事务转发层和独立 API/dependency 工具 Program。
- 公开 XML 解释实际行为、异常、返回值和 ownership；验证器拒绝占位 summary，不自动合成注释。

Windows 的真实库快照经过本次审阅后刷新，再由完整矩阵重新生成比较。
审阅包括 MiniAudio 的 S16/S24/S32 名称、混合类型位域的原生 bit offset 和 packed layout。
bimg 的 C header 与 C++ implementation 内容保持一致；managed API 使用当前命名与职责归属。

### 枚举实际调用回归

扩展独立 fixture 后，当前 .NET 9 Wasm 解释执行的 64 位整数调用通过，直接枚举调用触发签名不匹配。
修复将按值枚举转换为 IR 声明的整数 ABI 载体，保留语义调用接口和枚举指针。
该规则同时用于三种 import mode 和所有目标，不包含消费方或浏览器专用分支。
自定义枚举的 IR 大小也改为实际声明宽度。

14 项生成/布局回归及三个部署路径各 56 项真实调用通过。
五个 C 库和 bimg 的公开 API 快照不变，内部调用声明快照经差异复核后更新；
差异证据位于 `results/carrier-snapshot-review/`，完整矩阵重新生成并比较通过。

### Windows 目录发布回归

实际生成发现短期文件读者可能拒绝 staging rename。安装与回滚统一进入 BGCS.Core 自己的
内部目录发布边界，访问/共享拒绝最多重试两秒；持续拒绝保留原异常和旧输出。
没有引入消费方依赖、删文件绕过占用或无限等待。

目录事务专项 12/12 通过，其中三项新回归覆盖临时读者、持续占用、旧树恢复和释放后重试。
随后完整矩阵、三个部署路径及两项真实错误注入全部重新执行；上述表格和报告路径是终版结果。
`results/directory-publication-final.log` 和 `.trx` 保存专项证据。

最后同步 `.editorconfig` 的函数体左花括号规则，再编译完整 Release solution：零警告、零错误，
证据 `results/solution-editorconfig-final.log`。该配置调整没有修改手写 C#、生成规则或互操作 ABI。
可重建的示例输出及生成器锁文件已加入忽略规则；源码、配置和消费者仍可直接审阅。

### 最终 provider 归属核对

五个具体原生构建 provider 位于 `Build/Providers`，namespace 为 `BGCS.Cpp2C.Build.Providers`。
CLI、测试和文档同步；没有旧 namespace alias。公开 API 门禁先明确拒绝旧基线，随后逐项比对
五个类型的构造函数、方法和属性完全一致，再更新当前基线。
`results/provider-api-review.json` 保存该比对，首次拒绝日志保留为历史证据。

归属调整后的完整矩阵日志为 `results/full-matrix-plan-audit-final.log`，重新执行八个生产项目、
1018 项托管测试、实际 Windows provider、真实 C/C++ 库、API snapshot、package 消费与性能/供应链 gate。
独立 NativeAOT/Wasm 调用的 Runtime、生成算法和原生 ABI 没有在此归属调整中改变。

## 可重现命令

```bash
bash scripts/run-full-test-matrix.sh
python scripts/test-native-aot-bindings.py --dotnet /path/to/dotnet --compiler /path/to/clang
python scripts/test-wasm-bindings.py --dotnet /path/to/dotnet
python scripts/test-wasm-bindings.py --dotnet /path/to/dotnet --aot
python scripts/test-native-aot-bindings.py --dotnet /path/to/dotnet --inject-native-error
python scripts/test-wasm-bindings.py --dotnet /path/to/dotnet --inject-native-error
```

本机完整矩阵启用 `BGCS_REQUIRE_WINDOWS_NATIVE_PROVIDERS=1`，实际执行 Windows provider。
性能脚本强制执行冷生成和缓存预算；CI 另使用 `BGCS_ENFORCE_GENERATION_BUDGETS=1` 检查各真实库预算。
实际 `DOTNET_HOST_PATH` 和日志记录所选 SDK；路径必须按当前主机安装位置设置。

## 平台和发布边界

Windows 成功不代表 macOS/Linux 主机已经执行过终版。
CI 配置包含独立主机的 NativeAOT、Wasm 解释执行/AOT 和错误注入矩阵，当前没有执行远程 CI 或发布签名。
其他主机的真实库快照需要其独立 runner 审阅和验证。
当前内置 ABI 为 little endian；新增字节序需要目标描述和对应布局 lowering/运行向量。
未知 C++ ownership、allocator 或标准库特化仍需声明式 mapping、类型化 lowering 或明确的 C shim。
生成器不能推断头文件中不存在的生命周期事实。
