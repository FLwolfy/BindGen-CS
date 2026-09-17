# 验收规范

[Wiki](README.cn.md) | [English](acceptance.md)

本文是规范性文档。评分必须由测试产物生成，维护者不得手工指定分数。

## 总规则

每个分类包含十个等权证据点。只有对应自动 gate 在发布 commit 上通过才得分；明确支持半覆盖的 gate 可以得到 0.5 分。要求每项达到 **8.5-9.0**。任一 mandatory gate 失败都会把该项限制在 8.5 以下。跨平台验收暂缓；当前强制平台是 Windows x64，并在明确要求处验证 Windows x86 layout。

发布流程必须生成：

- `artifacts/acceptance/report.json`：机器可读结果；
- `artifacts/acceptance/report.md`：人工审阅摘要。

## 目标矩阵

| 方面 | 目标 | 强制 gate |
| --- | ---: | --- |
| 普通小型 C API | 8.5-9.0 | 生成编译率 100%、ABI 调用、生成源码零手改 |
| 中大型 C API | 8.5-9.0 | SDL3、miniaudio、cimgui 重新生成和编译 |
| 复杂 C ABI 正确性 | 8.5-9.0 | MSVC x86/x64 layout 和调用约定矩阵 |
| 普通 C++ class bridge | 8.5-9.0 | lifecycle/method 原生编译、链接和运行 |
| 复杂现代 C++ | 8.5-9.0 | 选定模板、STL、智能指针、virtual callback |
| 生成 API 美观程度 | 8.5-9.0 | Inno.Native 快照、analyzer、生成文件零手改 |
| 小白易用性 | 8.5-9.0 | 从 header 到验证输出最多五条命令 |
| 外层架构 | 8.5-9.0 | 自动项目依赖边界测试 |
| 内部架构 | 8.5-9.0 | 共享 IR、analyzer/emitter 独立测试 |
| NuGet/测试/发布工程化 | 8.5-9.0 | 干净包、symbols、确定性重复 pack、完整 Windows 矩阵 |

## 真实库性能预算

在固定 Windows CI runner、NuGet 已 warm restore 的条件下测量：

| 库 | 生成预算 | 强制结果 |
| --- | ---: | --- |
| cimguizmo | 15 秒 | C Bridge 和 C# 编译 |
| cimgui | 30 秒 | C# 编译和 API 快照 |
| SDL3 | 45 秒 | C# 编译和 API 快照 |
| miniaudio split header | 60 秒 | C# 编译和 API 快照 |

超时、OOM、无限 cache 增长或为了通过测试手工删除声明，都判定中大型 C 分类失败。

## ABI 证据

ABI 测试必须使用同一组头文件编译 native test DLL，并比较 managed 观察值：

- primitive size 和 signedness；
- enum underlying type；
- sequential、explicit、packed、nested、anonymous、aligned record；
- 一维/多维 fixed array 和 flexible array tail；
- signed、unsigned、zero-width、cross-unit、struct/union bitfield；
- pointer、reference、function pointer、callback、user data；
- cdecl、stdcall、适用时的 thiscall，以及 vectorcall diagnostics；
- borrowed、owned、caller-allocated 的 UTF-8/UTF-16 string；
- pointer/count、capacity/written-count、two-call query pattern。

只有生成代码编译成功、没有 native invocation，不算 ABI 证据。

## C++ 证据

C++ Bridge 必须编译、链接并执行以下测试：

- overloaded/default/deleted constructor 和 destructor；
- instance、const、static、virtual、pure virtual、overloaded method；
- namespace free function 和 policy 选中的 operator；
- single/multiple/virtual inheritance 与生成的 pointer adjustment；
- exception 捕获和 managed error 传播；
- 显式 class/function template instance；
- string、vector、span、optional、unique/shared pointer 和选定 variant adapter；
- 配置的 abstract callback interface 的 managed implementation；
- move-only 和 non-trivial return value 的安全 bridge storage。

无法支持的结构必须输出可执行诊断和可检查报告；静默生成 ABI 不安全签名直接判定失败。

## API 美观证据

- 生成文件绝不手工修改。
- 库特定行为必须放在 preset、policy 或生成目录外的 patch 中。
- public API snapshot 必须审阅并编译。
- 保留 raw imports；在 ownership 事实充分时，友好 API 使用 span、string、handle、result 和确定性命名。
- XML 文档包含有效 summary、param、typeparam、returns、ownership 和 lifetime 信息。

## 小白流程证据

下面流程必须能在空目录成功：

```bash
bindgen-cs init path/to/header.h
bindgen-cs doctor
bindgen-cs validate
bindgen-cs generate
bindgen-cs build
```

`doctor` 报告工具链问题；`validate` 只解析和验证 IR，不替换输出；`build` 编译生成 C# 和需要的 C Bridge。所有跳过声明必须汇总，并提供建议的配置或源码操作。

## 架构证据

自动架构测试必须保证：

- Runtime 不依赖 generator；
- Intermediate 不依赖 facade、emitter、Roslyn、filesystem 或 CLI；
- Analysis 不依赖 emitter 或 CLI；
- emitter 依赖 Intermediate contract，不依赖具体 facade；
- Tool 只包含命令组合；
- `CsCodeGenerator` 委托 Application，并只承担兼容 facade；
- BGCS 与 Cpp2C 共享 request、diagnostic、IR、output 和 emission contract。

## 包和发布证据

- 全部 package ID 使用同一版本。
- 干净 local feed 可以恢复 public package 和全部传递实现包。
- Tool 安装到空 tool path，并完成 init/generate/build smoke。
- 同一 commit 两次 clean pack 在排除 NuGet signature metadata 后内容等价。
- symbol package 和 repository metadata 完整。
- mandatory gate 全部通过前不得发布。

## 当前状态

强制 Windows x64 MSVC 范围已经通过，全部测量分类位于 8.5–9.0。`scripts/run-full-test-matrix.sh` 只有在 managed tests、native C/C++ runtime gate、四库生成/编译预算、源码与 reflection public API snapshot、NuGet/Tool smoke 全部通过后，才写入 `artifacts/acceptance/report.json`。其他 target ABI，以及 fixture 未构建的上游 DLL 直接调用，不属于已验证声明。
