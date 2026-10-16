# BGCS 3.0 七项工程评估

[English](assessment.md) | [能力与边界](capabilities.cn.md) | [验收](acceptance.cn.md)

评估日期：2026-10-16。依据是当前源码、公开接口审阅和记录明确的本地检查点，
不是已经完成的 3.0.0 发布认证。不再沿用旧架构的 macOS 报告或主观分数。
最终结论必须等待同 revision 首发矩阵、干净安装和发布门禁。

## 当前判断

BGCS 3.0 的分层、扩展接口与安全策略有实质实现和回归证据。它已经能处理丰富的 C ABI
和一组明确的 C++ 协议，但还不能据此宣布“所有功能在所有平台完全成熟”，也不能把目录
拆分完成等同于进入维护状态。

| 维度 | 当前评估 | 仍需正视的限制 |
| --- | --- | --- |
| 功能 | C/C++、三种 import、两种 Runtime 交付、实例/静态、插件、缓存、工作区和 MSBuild 有统一实现；复杂生命周期有实际 fixture。 | Raw、native holder 与友好托管投影的能力不同。任意模板、容器结果和未知 ownership 不会自动解决。功能 inventory 必须保留逐项当前证据。 |
| 代码优雅 | 不可变模型、有限类型化操作、明确失败路径和生成契约复核提升了可读性和可审查性；源码风格/API 门禁能约束回归。 | 复杂 ABI 与生命周期必然带来较多 planner/verifier/cleanup 代码。风格通过不是简洁或可维护性的数学证明，公共辅助面也需要长期契约维护。 |
| 架构整洁 | 十三个实现工程有依赖门禁；Frontend 的普通公开面已收敛，中立分析和 C# backend 不持有 Clang AST；Host 统一编排。 | 仍需持续防止 Host 变成所有语义的归属处。实现工程公开 helper 的增加要经过审阅，不能借友元或共享生产源码绕边界。 |
| 可扩展性 | 标准插件仅依赖 Contracts，支持源码输入、目标、变换、lowering、projection/backend、产物处理、验证和构建。扩展状态和外部输入参与缓存。 | 插件必须遵守形状、ownership、证据及指纹协议。高级 AST 插件刻意依赖精确解析器版本，并限制在 worker，不是同样稳定的普通端口。 |
| 使用简易性 | 普通用户使用分组配置及 init/doctor/validate/generate/build，C++ 自动串联 bridge；默认实例、FunctionTable、分文件，MSBuild 显式启用。 | SDK/编译器仍须安装；复杂 C++ 项目必须配置编码、allocator、模板实例、生命周期和 native build。不能承诺所有库只填一个头文件。 |
| 解耦性 | NativeModule、CppBridgeModule、InteropModule、ManagedApiModel 分离事实、桥接、ABI、托管呈现；进程协议隔离解析崩溃；Runtime 不带生成器。 | 换 frontend 必须提供同等完整的事实；换 ABI/provider 必须增加真实证明。接口解耦不会消除底层平台差异或证据要求。 |
| 跨平台性 | 宿主、目标、工具链、SDK、ABI、托管 runtime、import 分离；desktop/AOT/browser 有不同机制，GNU/musl 等资产不能混同。 | 只有同 revision 平台矩阵通过才可认证。Browser 非阻塞模式下拥有型 C++ virtual registration 当前仍省略，其他平台/浏览器也不能从 Windows/Chromium 推定。 |

这些维度不能用平均分抵消未通过的发布门禁。源码完整、本地测试和 release 认证应分别记录。

## 1. 如何扩展

普通扩展项目引用 `BGCS.Contracts`，实现所需端口并通过 `IBindingPlugin` 注册。
例如，新类型语义进入 `ICppTypeLowering`，变更托管呈现进入 `IManagedApiTransform`，
补充原生/托管源码进入 `ISourceContributor`，新目标进入 `INativeTargetProvider`，
新构建方式进入 `INativeBuildProvider`。仅需要有限 C++ 转换时，可以先选 recipe 或 native shim。

插件 manifest 声明契约范围、服务身份/优先级、设置 schema 和锁定资源；实际服务工厂按会话
创建并释放。通过 `ICacheFingerprintProvider` 声明状态与外部输入，无完整指纹时相应缓存
不会复用。CLI、SDK、workspace、MSBuild 读取同一配置。完整步骤与可运行示例见
[扩展](extensibility.cn.md)、[C++ 手册](cpp-extension-cookbook.cn.md)。

需要 AST 专属事实时才使用 `IClangFactContributor`：它依赖完整 SDK 的精确 Clang 高级面，
只在隔离 worker 执行，借用句柄不能跨出贡献调用。普通插件不必承担这些依赖。

## 2. 跨平台是否充分

平台模型不是用一个 `isWindows` 或 `isWasm` 拼接出来的；解析使用宿主 worker，头文件和
布局使用目标 SDK/ABI，原生 provider 使用实际编译器，生成策略使用 runtime/import 能力。
MSVC/GNU 等实际预处理及独立 native proof 用来检查最终构建没有改变声明、布局或常量。

因此基础架构是认真实现的，某个平台的完整支持却仍取决于真实运行和包消费。跨平台合理的
边界是：能证明的组合生成，不能证明的拒绝或要求 shim；不是为了列更多目标而假装 ABI 通用。

## 3. 如何做到解耦

Frontend 只构建中立原生事实；Analysis 判断选择、布局、语义和安全；Cpp2C 把 C++ 降为可复核
的 C ABI；CSharp 从 interop/managed 模型生成；NativeBuild 负责构建/资产；Runtime 负责调用
与生命周期。Host 组合它们，CLI/MSBuild 只把请求接入同一个入口。

Contracts/Runtime 无其他 BGCS 工程依赖；普通 lowering 不引用 Clang；解析在 worker 中。
注册顺序、输入与配置冻结，扩展结果还会经过独立契约复核。解耦依靠这些数据和执行边界，
不是依靠增加目录。唯一的 Frontend.Tests 友元已登记，生产友元仍被门禁拒绝。

## 4. 大多数 C/C++ 库是否能轻松绑定

公开接口为普通 C ABI、固定参数、可表达结构布局的库通常最直接。原始导入往往少量配置即可
生成；想获得 string/Span/owned object 等友好 API，还须提供头文件没有表达的语义。

C++ 需要区分“可生成 C ABI bridge”与“无需设计就成为自然 C# API”。常见对象、继承、具体
模板实例和已支持的标准协议已有路径；复杂模板、allocator、retained callback、异步取消、
第三方容器及不明生命周期通常需要项目配置、lowering 或 shim。没有调查范围与真实库证据，
不能给出“大多数 C++ 库都能轻松”的比例保证。受控 CPU fixture 的通过也不证明整个 GUI、
音频设备或 GPU 渲染系统都已绑定验收。

正确的完成标准仍是：针对要交付的库生成完整源码，warnings-as-errors 编译，真实 native
链接/调用，干净包消费，并把这些结果和准确功能范围绑定到同一 revision。
