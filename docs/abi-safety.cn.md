# ABI 安全与语义证据

[文档](README.cn.md) · [English](abi-safety.md) · [Runtime 生命周期](runtime-lifetime.cn.md)

BGCS 保留原生类型事实，直到最终 C ABI 与托管生成计划通过验证。熟悉的类型名、C# 编译成功或相同总大小都不证明调用安全；未知事实保持未知。

## 存储与调用分别证明

```mermaid
flowchart LR
    Native["原生 size/alignment/field offset"] --> Storage["托管存储计划"]
    Storage --> Fields["逐字段/数组 stride 核对"]
    Native --> Call["原生调用分类"]
    Runtime["运行时与 import mode"] --> Call
    Call --> Decision["允许已支持签名或要求 pointer/shim"]
    Fields --> Decision
```

分别验证原生存储、托管载体存储和按值调用 ABI。ExplicitLayout 能表示 offset，不会自动修复寄存器分类、栈对齐或运行时 marshalling。

当前 C# 规划允许支持的 primitive、enum、pointer 和固定 callback ABI。已解析的 .NET 10 CoreCLR/NativeAOT 聚合 profile 还可以采用 Windows x64 或 System V AMD64 的已实现调用算法；这个机制描述不等于平台认证收据。

规划递归核对标量载体、数组 stride、完整字段、重叠规则、size 与托管 alignment。可表示时 explicit layout 采用匹配的 packing；生成的外层 alignment probe 在创建上下文时检查实际托管字段位置。ABI manifest 记录 runtime profile、storage packing 和调用分类。

Windows x64 的普通小聚合按大小分类；System V AMD64 还根据真实浮点/整数成员进行 eightbyte 分类，未对齐字段或较大的记录使用 memory 形式。不能只根据 `sizeof` 推出调用兼容；见 [Microsoft x64 ABI](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention?view=msvc-170) 与 [.NET 10 分类实现](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/vm/methodtablebuilder.cpp)。

未知 profile、无法匹配的 alignment、位字段、柔性数组、空/不完整记录与特殊标量仍要求 pointer 或 native bridge。Browser 和其他 CPU 的聚合 profile 尚未开放，需要先实现并验证对应调用路径。测试必须真实执行参数、返回值和反向回调，并覆盖 import mode 与托管执行模型，不能以编译成功代替。

嵌套过度对齐回归分别验证 `Outer.size = 96`、`Outer.nested = 32`、`Outer.suffix = 64`。总大小正确但字段错误仍是错误。普通托管分配不承诺任意原生对齐；需要时使用配对 aligned allocator，并核对每个指针的对齐。

## 事实与证据

| 证据 | 含义 |
| --- | --- |
| Unknown | 缺失信息。 |
| Inferred | 规则或启发式推断。 |
| Declared | 结构化原生输入声明。 |
| UserAsserted | 用户明确声明。 |
| Verified | 规定的独立验证建立事实。 |

证据保存来源。用户信任配置不把声明提升为 Verified。Create/Destroy 名称不验证 allocator，enum 名称不验证宽度。

模型保留稳定 ID、alias 身份和底层类型、逐层指针 qualifier、数组维度、enum carrier、字段/位 offset、调用约定、目标字节序和来源。变换必须保留 ABI 身份，或采用明确支持的 bridge operation。

## 基础类型与特殊 scalar

`char` 符号性、`long`、`size_t`、指针、bool、enum 底层类型和 `wchar_t` 来自实际目标。Windows LLP64 不等于 Linux LP64，wasm32 指针不等于生成器宿主指针。

`long double`、half/BFloat16、128-bit scalar 或 `va_list` 的存储表示不意味着支持按值调用。16 字节托管载体不能证明 SysV x87 `long double` 传参/返回安全。shim 可使用指针/字节或明确转换，但不能静默替换成 `double` 丢失语义。

原生固定宽度 enum 和 `api.enums` 保留完整 signed/unsigned 范围。`api.enumMappings` 通过宽度与符号核对后改变 friendly 展示，raw 标量 ABI 不变。成员使用十进制字符串，避免 JSON 浮点丢失 UInt64 高位和最大值。

## 文本语义

`char*` 不自动表示 UTF-8。`StringContract` 描述编码、code-unit 宽度/字节序、长度单位、方向、null/零终止、保留地址、lifetime，以及需要时的所有权/释放。

两字节 `wchar_t` 可采用明确 UTF-16 契约，四字节需要 UTF-32。`"AB"` 的 UTF-32 首个 code point 是 65，不能把两个 UTF-16 合并成 4325441。补充平面字符、严格非法 Unicode 处理、终止符与原生长度单位需分别验证。

临时输入字符串投影要求 read-only、明确不保留地址的同步调用 lifetime，以及明确的 null 策略。支持无长度通道的零终止输入，或明确不带终止符、具有一个独立普通整数输入长度参数的计数输入。计数路径在原生调用前核对编码宽度、字节序、字节／code-unit 长度单位、正值 code-unit 上限及原生整数范围。共享计数、固定长度、保留地址／异步文本和相互冲突的地址协议需要另行支持的 projection/shim；可表达的 raw ABI 仍然保留。

## Buffer、所有权、callback 与 async

Buffer 契约描述 element、bounds/length 单位、direction、保留地址、null 和 required alignment。Pinned Span 转换要求支持的固定 element 存储、同步 call lifetime 和精确 length 参数。不能假定独立 capacity 协议或 flexible tail 是普通 Span。

Owned result 要有匹配的 release function/domain 和 lifetime。非零借用指针不会自动获得析构责任。Callback unregister 必须禁止新调用并同步原生使用；取消不允许在 native terminal 确认前释放 pin。Runtime 帮助实现明确协议，不代表自动生成全部 callback/async 投影。见 [Runtime 生命周期](runtime-lifetime.cn.md)。

缺少语义不虚构 friendly wrapper；可表达的 raw ABI 仍有用。明确请求的不安全或不可表达投影会失败，不能悄悄改变契约。

## 数组、union、位字段与 varargs

完整、受支持的固定 callback prototype 使用精确函数指针。可变参数、未声明参数列表或无法证明托管调用形式的 callback 可以作为 `nint` 保存、传递和返回；原始 prototype 与不能直接调用的原因保留在 ABI metadata 的 `opaqueCallbackAddresses`。这条路径只借用地址，不把未知或可变签名伪装成固定调用。需要从 C# 调用它时，必须提供原生固定签名 adapter。普通可变参数导出仍要求固定 wrapper；不会因此获得任意 vararg 调用能力。

维度、固定 stride 和 incomplete/flexible tail 保持明确。Union 重叠与每个 offset 匹配原生事实。位字段使用精确 bit offset、width 和符号扩展；当前 backend 访问要求已证明 little-endian。未知或大端需要原生 adapter。

可变参数和无明确 prototype 的调用要求固定签名 native wrapper。FunctionTable 不提供任意 varargs；存在 `va_list` 载体不等于通用可移植调用实现。

## 桥接与实际编译器验证

C++ operation 表达构造、析构、native cast、参数/返回转换和异常边界。多继承采用真实原生 cast，不假定对象地址相同。异常在支持的 C ABI status/error 边界停止，cleanup 保持明确 allocator 所有权。

生成 C header 重新解析后比较导出名称、参数和返回。所有权/error/lifetime 按稳定身份合并，不因重解析丢失。受控源码 fragment 声明精确内容和输入，并接受 bridge 与原生构建检查。

源码构建比较有效声明，并用实际选择的编译器编译 trait/layout guard。Header、response/config、可执行文件、库和搜索输入锁定且发布前复核，以检测 parser/builder 的宏和布局差异。这不能代替真实 round trip。预编译输入需要明确目标来源和验证，仅检查 CPU 格式不能证明 libc ABI。

有效声明把参数书写形式的数组维度与调整后的 pointer 调用签名分别保存，同时核对 inline、definition 和 linkage 事实。已选择的标量对象宏只在预处理输入中物化：实际原生编译器先展开自己的头文件分支与宏定义，事实 frontend 再核对 scalar kind、宽度、符号与值；const variable 直接比较其值。IEEE 值比较精确位模式，因此正零和负零不同。这些 frontend 常量保持 Declared 证据，不能据此声称已经独立证明所有编译器的浮点算术与优化行为。

MSVC 预处理的 line directive 不携带 Clang 的 system-header 标记。事实 adapter 只对已解析 SDK include 根恢复该元数据，保留源码 token 和用户头分类；它还用仅供 parser 的非 builtin 名称保留一个冲突的 MSVC intrinsic 声明。少量未实现的 dependent type/member intrinsic 可出现在未实例化的 SDK 模板中；任何具体实例化都触发硬错误，包括 SFINAE，不能采用猜测值。未知 intrinsic、parser 无法求值的具体使用以及不匹配声明都阻止构建。这一有界兼容机制不表示 Clang 能解析任意 MSVC/GCC 扩展。

GNU C++ 的系统头可能包含与固定 Clang frontend 不同的编译器扩展。适配器只允许把用户声明之前、具有实际依赖和系统来源证据的完整工具链头文件树转成独立声明前导。用户 token 仍来自所选 GNU 编译器，最终宏状态通过独立 `-dM` 操作取得。前导声明在 worker 私有 PCH 中存活至事实提取结束；清除它的宏环境后才解析已经展开的用户 token。PCH、Clang 句柄和专有 AST 不进入 Contracts。

这条路径不是任意 GCC 源码转换：交错在用户声明之后的工具链 include、工具链头中嵌套的用户头、无法证明的 pack/pragma 历史、用户修改 compiler builtin，或依赖动态 builtin 的 include-entry 宏都会被明确拒绝。调用者应调整头文件边界或提供能准确描述该语言扩展的 frontend/shim；不能用相同大小的浮点类型替换 GNU 特殊类型来通过解析。

使用声明前导的事实带有 `requiresNativeCompilerProof`。Host 在 `generate`、`bridge` 和所有 `build` 模式发布前，都要求实际所选原生编译器检查原始命名类型的规范身份、变量类型、枚举和已选择标量常量，并编译 storage guard 与 bridge。常量在支持头文件载入前冻结，浮点值比较精确 IEEE 位。最终构建参数另行接受有效事实和字符串字节检查。对象构建可能需要 relocatable link；这些证明既不表示完成最终库链接，也不表示真实调用或全部 ABI 已认证。无法证明的公开类型明确失败，私有 opaque 实现不会被当作可公开命名的原生类型。

## Import 与最终源码

FunctionTable、DllImport 和 LibraryImport 共享 interop/语义模型，但各自运行时签名能力不同。FunctionTable 必需导出完整验证。外部 ABI 字符串属于声明，不是独立调用证明。

Processor 在最终源码契约验证前运行，缓存命中也复核实际 import 签名、layout 和 convention strategy。能编译的 rewrite 不能暗改冻结 ABI。

浏览器 wasm32 使用类型化静态模块能力。只有已验证 browser static profile 的 Cdecl LibraryImport 可省略冗余显式 convention metadata；不同约定或 profile 改动被拒绝。其他 import 保留显式策略。该有限选择由同一真实浏览器 AOT fixture 验证，不修改生成输出绕过测试。

## 验收证据

独立 NativeAOT、浏览器解释/AOT fixture 保留三种 import 的全部 74 项检查，包括 scalar、UInt64 高位 enum、原生 long/bool、layout/bitfield 指针、bounds/capacity、ref/ref、in/out、记录字段与 Span/ref 的原地址别名、handle、callback/null/release、生成托管 callback 的状态捕获与原始异常返回、C++ 对象/继承，以及不完整/借用/缺失导出函数表。失败注入改变原生返回结果，必须在真实调用失败，不能仅因无关构建失败而通过。

NativeAOT 额外要求 39 项 aggregate 检查，合计 113 项。三种 import 都调用 C17 中的小型、混合、大型、union、嵌套、同类浮点、内联数组、紧密未对齐和单浮点成员记录的变换，包括按值记录的反向回调。每种 import 的结果签名必须与独立编译的原生 C probe 相同。这是桌面发布门禁，浏览器 aggregate 能力单独认证。

收据记录 source revision/dirty 状态、真实 generator/Runtime/worker 字节、目标、运行时、编译器/SDK/workload 和检查项。本机 Windows 不能认证 Linux、macOS、所有 ABI 特性或安装产品。支持声明和同 revision 发布门禁以[测试](testing.cn.md)、[实施状态](implementation-status.cn.md)和[功能 inventory](../eng/acceptance/feature-inventory.json)为依据。
