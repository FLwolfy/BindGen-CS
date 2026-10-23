# 配置指南

[文档首页](README.cn.md) | [English](configuration-guide.md) | [自动生成的字段参考](generated/config-reference.cn.md)

BGCS 4.0 的 C 与 C++ 使用同一份严格分组的 `bindgen.json`。生成器、Runtime 和默认消费者
采用 .NET 10 / C# 14。配置冻结成会话输入，不在其中保存服务实例或解析器句柄。
当前安装版本的完整 schema 是字段名称和默认值的权威来源：

```bash
bindgen-cs schema bindgen.schema.json
bindgen-cs schema workspace.schema.json --kind workspace
bindgen-cs schema extension.schema.json --kind extension
```

[英文参考](generated/config-reference.md)和[中文参考](generated/config-reference.cn.md)
来自同一份实际 CLI schema。`scripts/generate-config-docs.py` 使用已经编译的工具，在所属
临时目录导出 schema，并通过 `--check` 拒绝过期文档；不会根据旧测试目录名称推断新行为。

schema 拒绝未知内置字段。插件选项只有在 bootstrap 注册所属 namespace 的 schema 后才被
接受，不提供全局忽略未知字段的开关。仅接受 `schemaVersion: 2`，不读取旧平铺配置或
旧 facade API。文档与 fragment 的 schema 校验之外，还会对组合后的完整输入、库身份、
目标、语义和执行限制进行验证。

## 默认值与完整小请求

| 决策 | 默认值 | 显式选择 |
| --- | --- | --- |
| C 标准 | `c17` | 声明一个受支持的源码标准 |
| C++ 标准 | `c++23` | 声明一个受支持的源码标准 |
| 原生目标 | `host` 解析为具体 ABI | 声明目标家族与 SDK |
| API 风格 | `instance` | 显式共享 `static` 风格 |
| 导入 | `functionTable` | `dllImport`、`libraryImport` |
| 框架 | `net10.0` | 当前大版本 schema 固定此基线 |
| Runtime 交付 | `package` | `source` |
| 托管布局 | `split` | `singleFile`，默认 `Bindings.cs` |
| 缓存策略 | 启用 | 可以关闭；复用仍须证明输入完整 |

```json
{
  "schemaVersion": 2,
  "library": {
    "id": "sample",
    "nativeName": "sample"
  },
  "source": {
    "language": "c",
    "languageStandard": "c17",
    "headers": [
      "include/sample.h"
    ],
    "includeDirectories": [
      "include"
    ]
  },
  "targets": [
    {
      "id": "host"
    }
  ],
  "api": {
    "namespace": "Example.Sample",
    "name": "SampleApi",
    "names": {
      "sample_add": "Add"
    }
  },
  "output": {
    "root": "Generated"
  }
}
```

`generate` 产生经过验证的托管产物和按需 C++ bridge 源码；消费者在外部编译、链接、
打包和部署。生成、消费者编译、真实原生调用是不同的证据阶段。

## 源码与语言事实

`source.headers` 是有序入口头文件。`includeDirectories` 和 `systemIncludeDirectories`
是有序搜索根，不能与声明选择策略混为一谈。`includeTransitivelyReferencedHeaders`
保留传递声明；`includeSystemDeclarations` 显式保留系统声明。
`defines` 和 `arguments` 必须符合实际原生构建。`preamble` 和 `postamble` 是明确的解析
输入，并参与内容身份。

声明的用户 include 根也决定生成原生源码中的可移植引用。BGCS 核对实际搜索顺序；歧义或根外输入明确失败，不回退到工作区绝对路径。类型化源码要求与冻结输入根映射见[原生源码输入](native-source-inputs.cn.md)。

C 的 `includeHeaderHelpers` 默认为 `false`。普通绑定只导入外部 C 函数及其别名，
不要求现有库额外提供头文件辅助导出。设为 `true` 后，选中的 static、inline 和仅可直接
调用的 C 辅助函数会生成 adapter，必须和外部函数一起编译到配置指定的同一个原生模块。
精确选择辅助函数或将其用作必需 cleanup 时，未启用该项会给出明确诊断。显式
`variadicVariants` 请求的固定 adapter 独立保留；C++ 继续使用普通 C++ bridge。

`parseMacros` 和 `parseComments` 决定事实保留，只在确实不需要对应输出时关闭。
聚合头文件可以包含整个公开接口，无须列出每个子头。来源身份、typedef、限定符、
嵌套数组和调用约定作为原生事实保留到后续分析。

正式输出与缓存须位于显式 include/source 根以外。与配置并列的头文件不需要
`includeDirectories: ["."]`。需要显式 include 根时，采用独立 `include/`、`src/`、
`Generated/` 目录。读取输入不会静默改变进程当前目录。

## 目标、SDK 与托管运行环境

每个 `targets[]` 有 `id`、可选 `triple` 和 `settings`。settings 包括
`compilerPath`、`sysRoot`、普通及 C++ 系统头目录、宏定义和解析参数。
简单程序名保留为发现请求；路径归属于声明该值的配置目录。原生目标 ID 表达 OS、CPU
和 ABI；NuGet RID 表达资产选择，不能代替完整目标事实。

worker 根据**宿主**选择。目标头文件、基础类型宽度、布局、调用 ABI 和 SDK
根据**目标**选择。Clang 解析不要求最终编译器也是 Clang；使用 MSVC 或 GNU 时仍需匹配
头文件和目标 ABI。跨目标请求不能混入宿主标准库头。

`api.runtime` 使用开放 ID，例如 `dotnet.coreclr`、`dotnet.native-aot`、
`dotnet.mono-wasm`；选择额外实现前需要显式注册。默认组合只为受支持的桌面目标
提供 CoreCLR 默认值；自定义和额外目标需要适用的显式 profile。托管运行时和导入方式
共同参与能力判断。浏览器 Wasm 输出由消费者链接的静态导出表 adapter。
GNU 与 musl 保持不同 ABI 身份。SDK 版本、设备和模拟器要求不能由短 target/RID 证明。

通过 `doctor`、`inspect` 与[能力证据](capabilities.cn.md)检查实际解析的事实。
缺失前提会形成诊断，不会隐式下载 SDK 或编译器。

## API 命名、导入与文件布局

`api.namespace`、`name`、`names` 控制托管身份，不参与原生解析。映射使用准确原生名称
或无歧义的限定 callable 名。C++ 导出名覆盖属于 `bridge.callableLowerings`；只修改托管名称
不能同时改掉原生 ABI。

所有导入方式使用同一份已检查的 interop 契约。FunctionTable 完整加载必需符号，支持
明确的 module provider 和独立实例。DllImport 与 LibraryImport 使用各自受支持的导入和
marshalling 能力；LibraryImport 验证也检查其生成源码。选择导入方式不会绕过未支持的
按值布局、回调规则或特殊标量 ABI。

实例上下文提供 `Raw` 与符合条件的友好方法。FunctionTable 使用 `Load()` 或
`FromProvider(...)`，DllImport/LibraryImport 使用 `Create()`。
显式 static 风格具有共享生命周期语义。消费者启用 unsafe，并编译所选目标 manifest
列出的准确托管输入。

`output.layout = split` 根据职责拆分 API、imports、types、callbacks 和 helpers。
`singleFile` 合成到 `output.singleFileName`；名称必须是安全的 `.cs` 文件名，不能是路径。
原生和 metadata 文件仍有独立 manifest 条目。生成输出是一套联合所属产物。
解析、ABI 验证、编译或发布失败时，保留上一套完整产物。

`api.enums` 声明托管 enum 表现，必须明确固定宽度的底层类型及十进制成员值。
`api.enumMappings` 选择准确的原生 typedef、enum 或标量名称。映射必须保持宽度和符号性，
`Raw` 继续采用原始原生载体。例如 C 的 `uint64_t` typedef 可以保留高位标志：

```json
{
  "api": {
    "enums": [
      {
        "name": "WideFlags",
        "underlyingType": "uInt64",
        "isFlags": true,
        "values": [
          {
            "name": "HighBit",
            "value": "1152921504606846976"
          }
        ]
      }
    ],
    "enumMappings": {
      "bgcs_wide_flags": "WideFlags"
    }
  }
}
```

十进制字符串避免配置序列化损失 64 位整数精度。省略底层类型、重复成员、越界值或不兼容的
载体均会验证失败；给类型取 enum 名称不能证明原生存储或按值调用兼容。

## 使用已有的托管值类型

`api.externalTypes` 通过明确的逐字段复制，把精确的原生 struct 映射到闭合的消费者类型：

```json
{
  "api": {
    "externalTypes": [
      {
        "nativeType": "NativeVector_int",
        "managedType": "Consumer.NativeVector<int>",
        "fields": {
          "size": "Size",
          "capacity": "Capacity",
          "data": "Data"
        }
      }
    ],
    "projectionInputs": {
      "sources": [
        "ConsumerTypes.cs"
      ]
    }
  }
}
```

静态 Roslyn 绑定从冻结的 `api.projectionInputs` 源码/引用元数据解析完整 public 可写标量字段，
不运行消费者或测量 CLR offset/alignment。字段顺序可以不同：
原始导入继续使用生成的原生载体，并单独检查按值调用 ABI。不会仅凭大小相同，就把
外部类型直接放进导入签名。

定宽整数和浮点字段必须保持种类、符号及宽度。数据地址字段可以使用非托管指针、
`nint` 或 `nuint`，但声明的指针种类和目标宽度必须匹配；复制 packed 字段不假设
地址天然对齐。复制地址不会保留指向的存储，也不转移所有权。调用者仍需维护真正的
对象生命周期，并在有效范围内进行 unsafe 访问。等宽的 `long` 不能冒充地址声明。
函数指针、字段重叠、嵌套聚合与缺乏证明的存储需要独立的类型化 adapter。逐字段复制
不能保持原始 `in`、`ref`、`out` 地址别名。

## 字符串、缓冲区、拥有型结果与证据

`char*` 不能证明 UTF-8、终止符、保留行为或所有权。`wchar_t` 宽度来自目标，2 字节与
4 字节文本需要不同 codec。完整字符串声明示例：

```json
{
  "semantics": {
    "stringParameters": [
      {
        "function": "sample_set_name",
        "parameter": "name",
        "encoding": "utf8",
        "codeUnitWidth": 1,
        "byteOrder": "native",
        "lengthUnit": "codeUnits",
        "direction": "input",
        "lifetime": "call",
        "zeroTerminated": true,
        "retainsPointer": false,
        "allowsNull": false
      }
    ]
  }
}
```

有界缓冲区使用 `semantics.bufferParameters`，声明准确参数、`lengthParameter`、
`lengthUnit`、访问方向、生命周期、保留和 null 策略。可选 `requiredAlignment` 必须符合
原生存储需求。Span 包装只能覆盖已经声明的访问窗口；原生保留指针需要独立生命周期
owner。字节、code unit、Unicode scalar 和类型元素长度不能互换。

可写容量协议可以改用 `capacityParameter`，再通过 `writtenCountParameter` 指向独立的
原生输出整数指针。生成方法接收 `Span<T>`，以 `out int written` 返回**元素数量**。
BGCS 使用 checked 算术把 Span 容量转换成声明的原生单位，并拒绝负数、超过容量或不包含
完整元素的原生输出数量。容量与输出数量共用 `lengthUnit`。独立的已初始化输入长度与可写
容量需要显式 adapter，不能静默当作同一个范围。

单个必需值指针使用 `semantics.valueParameters`，显式声明 `direction`
（`input`、`output`、`inputOutput`）、`lifetime: "call"`、`retainsPointer: false` 和
`allowsNull: false`。受支持的标量与经过检查的生成记录载体分别形成 `in T`、`out T` 或
`ref T`。原始存储在整个调用内固定，并在进入原生代码前检查实际地址对齐。引用别名、
记录与字段重叠、以及和借用 Span 的重叠保留真实地址与写入顺序。`out` 借用不会先把
可能同时作为输入的存储清零。外部字段复制投影不能保证这种引用身份，因此明确诊断并
省略该便利接口。经过验证的 written count 是转换后的副本，不得和借用值、Span 或另一个
count 输出重叠；重叠请求在原生调用前拒绝。raw 导入仍保留原始指针。
显式注解且原生布局为一字节的 `_Bool` 可生成 `in bool`、`out bool` 或 `ref bool`，
raw 指针继续为 `byte*`。便利接口固定原始 Boolean 存储，保留引用别名，以及后续结果检查
抛出异常之前已经发生的原生写入。原生和请求的对齐必须都为一字节。Windows `BOOL`
等整数约定、未知 Boolean 布局、可空、保留、异步、过度对齐、enum 与指针到指针的便利接口
需要明确支持的 adapter。C++ bool 指针 lowering 属于独立的原生桥接能力，大小相同不足以
证明 C++ 指针重解释安全。
别名、函数视图与共享 facade 保留引用修饰符。包装不能阻止原生代码越界写入；原生库必须遵守
声明的容量、访问与生命周期契约。

可选指针还可以在经过检查的重载中永久绑定为 NULL。为该参数声明
`semantics.valueParameters`，设置 `allowsNull: true`、`lifetime: "call"`、
`retainsPointer: false` 及实际访问方向，再添加 `api.functions[].defaults` 条目，
例如 `{ "parameter": "p_open", "value": { "kind": "null" } }`。
BGCS 只在固定 NULL 的 facade 中隐藏这个指针，其他字符串、值和 buffer 输入仍经过
正常检查。其他显式默认值可以继续形成重载；别名、命名视图与共享 facade 使用同一个
经过检查的操作。这不会创建通用的可空 `ref T` 重载；完整指针签名继续存在于 `Raw`。
NULL 默认值本身不能代替缺失的可空性、保留指针或生命周期事实。

`api.records[].constructorFields` 选择托管数据载体构造函数。准确固定长度的原生基本数值
数组字段还会获得一个 `ReadOnlySpan<T>` 复制重载。所有数组参数都必须恰好包含原生规定的
元素数量，任何字段赋值之前先检查全部长度。构造函数把数据复制到记录自身独立的 inline
存储，同时保留接受 raw 数组载体的构造函数。它不会构造原生 C++ 对象，也不会把拥有型原生
对象的内存作为 Span 借出。指针、Boolean、enum、记录及特殊标量数组继续使用 raw 构造函数，
并明确记录不提供 Span 便利转换的原因。

`semantics.ownedResults` 声明结果的匹配 `releaseFunction`、释放/分配域和
`releaseThreadDomain`。只有清理实际不受线程限制时才使用 `any`；`unknown` 不允许
假定无限制自动清理。创建与释放必须使用同一个域；拥有型对象保留其析构所需模块。
[NativeShim 示例](../examples/NativeShim/README.md)在没有自动所有权契约时展示明确的
raw 清理。

配置声明的证据等级是 `UserAsserted`。源码声明事实和独立验证保持不同证据等级。
用户声明被接受不会自动成为 `Verified`。未知友好语义抑制不安全便利方法；正确可表达的
raw ABI 可以保留。原生 ABI 自身不受支持时必须失败。大小相同不能证明字段偏移、对齐或
按值寄存器分类。`long double` 等特殊标量需要已支持证据，或者指针/shim 路径。

长期 callback 需要 unregister 与 quiescence 协议。异步取消后必须收到 native 的终止
确认，才能释放 pin 或模块 lease。这些事实可以通过中立 interop 端口贡献；配置名称
不能创造尚未实现的安全保证。使用特定 callback/async 投影前检查实际功能清单与测试。

## C++ 类型化 recipe、模板与 shim

`cpp` 分组包含 `typeLowerings`、`callableLowerings`、`templateInstantiations`、
`nativeShims`、`virtualCallbacks`。lowering 构建类型化 bridge operation，生成 C header
和 C++ source，再解析实际 C header 并复核导出契约。所有权和转换语义通过稳定身份保留，
不会因重新解析丢失。

type recipe 指定匹配的原生类型、准确 C ABI carrier、参数/结果转换，以及可选的明确指纹
helper fragment。managed projection 添加公开托管类型与转换声明，同时保留准确 raw carrier。
每份 managed expression 都会检查语法，即使类型 pattern 从未匹配。
`size_t` 等目标相关 carrier 使用实际目标事实，不使用生成器宿主宽度。

callable recipe 选择限定名称，可修改导出名、排除声明，或者包装独立规划的调用。
`templateInstantiations` 选择具体类和固定函数模板。native shim 声明公开 C header、
实现源码和准确符号，随后使用普通解析、生成与发布阶段。抽象虚接口回调需要明确有限
callback slot 和 native proxy 生命周期协议。

具体 DTO 见[C++ cookbook](cpp-extension-cookbook.cn.md)、[lowering 架构](lowering.cn.md)、
[Core-only 插件](../examples/LoweringPlugin/README.md)和
[自动生成的 C++ 分组参考](generated/cpp-config-reference.cn.md)。不支持的可变参数签名
需要遵守 C argument promotion 的明确固定 native wrapper。
非平凡 C++ record 不能只为使生成编译通过，就映射成同大小的托管结构。

## 静态托管投影输入

`api.projectionInputs.sources`、`references`、`defines` 是投影、编译期常量与外部值类型分析使用的静态输入，不是构建指令。手写 partial 文件与外部类型由消费者工程自行编译。原生库与生成的 bridge 由外部构建系统编译、链接和部署。配置没有原生或托管项目构建分组。

## Runtime 包与源码交付

`runtime.mode = package` 引用独立 `BGCS.Runtime` 消费库，没有生成器/解析器依赖。
`source` 在 `runtime.namespace` 或默认 `<api.namespace>.Runtime` 下发出所需源码闭包。
namespace 改写采用结构化方式，单文件布局可以将闭包合入 `Bindings.cs`。

包与源码形式必须通过同一套 codec、module、handle 和 callback 测试。
一份绑定避免携带重复 Runtime 实现。多个源码 Runtime 绑定默认使用各自 API namespace；
选择自定义 namespace 时须明确处理冲突。

## 本地组合、profile 与路径归属

`extends` 是本地配置路径列表，按声明顺序组合。路径相对于声明它的文档，URL 不被加载。命名 profile 在本地组合后应用；循环、未知字段和冲突必须明确修复。`GenerationRequest.FromFile(path)` 将读取延后到宿主选定后；将令牌传给 `GenerateAsync`，即可同时取消配置读取与生成。

## 插件 bootstrap 与缓存策略

`extensions.plugins` 列出严格 plugin manifest。每份 manifest 声明准确公开入口类型、
兼容 major、服务和完整可执行 asset hash。bootstrap 在验证插件选项与自定义目标前加载
descriptor/schema。会话 factory 注册中立服务后冻结 registry；重复、独占匹配和输出路径
冲突会确定性失败。

普通插件使用 `BGCS.Core`。Clang 专有事实 contributor 是明确 worker-only 的高级
扩展。Host 与 worker 资源归属分离。普通进程或加载边界不构成安全沙箱。

影响产物的状态需要 `ICacheFingerprintProvider`，包括外部输入内容和存在性。
缺少指纹时禁用受影响缓存复用。priority、注册顺序、配置、代码、header/搜索目录成员、
target、toolchain、Runtime 模式与 processor 都参与生成身份。
报告中的实际 hit/miss/not-used 才是执行结果：启用策略本身不能证明发生了命中。

可复用阶段包括内置 frontend 的原始解析结果与内置 C# emission。解析复用还要求
完整 worker/资源闭包、固定 .NET 执行环境、有效参数、源码内容和 include 搜索目录
成员关系。无法建立闭包的环境或编译器输入会禁用解析复用。源码 contributor 在查询前
执行，其实际捕获字节参与身份。托管投影、源码处理和最终源码契约验证仍会执行。不受支持的 frontend/backend、缺少完整上游指纹或处理器缺少完整指纹时禁用该阶段，
不会复用不确定的字节。有完整指纹的处理器在查询前激活并声明输入，命中或未命中时
都会执行。条目验证完整文件名 / 内容集合，损坏条目作为 miss 处理。
`cache.maximumBytes` 默认 512 MiB，`cache.maximumEntryBytes` 默认 64 MiB；都必须为正，
单条目最大 256 MiB，总容量至少容纳一个条目。缓存命中不能跳过本次生成所需的最终契约和输入验证。

## 执行、工作区与发布

`execution` 包含正数的生成会话 deadline。

`execution.diagnostics` 决定候选阶段的警告是否阻断发布。例如：

```json
{
  "execution": {
    "diagnostics": {
      "warningsAsErrors": false,
      "errorCodes": [
        "BGCS-SAFETY-OWNERSHIP"
      ]
    }
  }
}
```

默认保留警告；`errorCodes` 按完整代码精确匹配，不使用通配符。`warningsAsErrors: true`
将候选门禁遇到的所有警告提升为错误，包括扩展 validator 的警告。该策略不能降低 ABI 错误，
也不由控制台日志过滤选项控制。返回数据指针缺少明确所有权或生命周期证据时会报告
`BGCS-SAFETY-OWNERSHIP`；默认仍保留正确的 raw ABI，不根据函数名推断拥有关系或字符串编码。
需要 friendly API 时声明 `semantics.ownedResults`、`semantics.borrowedResults` 或提供中立插件契约。
声明保持 UserAsserted，不会因选择严格模式成为 Verified。提升后的错误在正式发布前阻止
替换上一套完整产物；发布完成后的备份清理、私有源码存储清理或缓存写入提示仍是后处理诊断，不能撤销已经完成的发布。

Ctrl+C 与 SDK 取消传给 Host 及 worker/native 进程树。取消或验证失败不能发布部分成功
产物。

```json
{
  "schemaVersion": 1,
  "parallelism": 2,
  "projects": [
    {
      "id": "core",
      "configuration": "core/bindgen.json"
    },
    {
      "id": "addon",
      "configuration": "addon/bindgen.json",
      "dependencies": [
        "core"
      ]
    }
  ]
}
```

workspace ID 唯一，依赖明确，执行前拒绝环路。输出不能互相重叠，也不能与受保护的
input/cache 根重叠。generate 根据依赖图执行，失败的前置项目阻止依赖者发布。
`diff` 使用私有预览存储，保持正式输出；`inspect` 提供脱离解析器的 native、bridge、
interop 和 managed 事实供审查。

发布依赖所属标记、hash、锁和恢复 journal。不能使用未归属的非空目录、文件系统根，
或经 symlink 逃逸的路径。临时验证输出是私有存储，最终 manifest 管理准确文件集合与
过期生成文件。手写源码放在该所属边界以外。

发布 revision 的功能清单、schema 文档、package closure 和实际平台收据必须一致。
本指南描述当前输入与保守边界；实际实现/认证状态决定完整 3.0 维护门禁是否通过。

控制台诊断可以采用 `--log-level warning`，并通过 `--frontend-log-level error` 单独控制
frontend 提示。支持 `trace`、`debug`、`information`（或 `info`）、`warning`、`error`、
`critical`、`none`。失败诊断始终显示；展示偏好不改变成功状态，也不删除 JSON/Host 结果中的
诊断。这些设置属于命令选项，不参与生成输入或缓存身份。

## C 指针 factory 与显式成员方法

所有权与展示是独立输入。对于 `Widget* widget_create(void)`、`void widget_destroy(Widget*)` 和
`int widget_get(Widget* self)`，在常规分组配置中加入：

```json
{
  "semantics": {
    "ownedResults": [
      {
        "function": "widget_create",
        "releaseFunction": "widget_destroy",
        "releaseDomain": "widget",
        "releaseThreadDomain": "any"
      }
    ],
    "borrowedParameters": [
      {
        "function": "widget_get",
        "parameter": "self",
        "lifetime": "call",
        "retainsPointer": false
      }
    ]
  },
  "api": {
    "memberFunctions": [
      {
        "function": "widget_get",
        "nativeType": "Widget",
        "receiver": "self",
        "objectName": "WidgetObject",
        "name": "Get",
        "releaseDomain": "widget"
      }
    ]
  }
}
```

factory 返回拥有型对象，既提供 `widget.Get()`，也提供经过检查的上下文 facade。
成员的 `releaseDomain` 可省略；指定时，即使原生指针类型相同，也会拒绝其他分配／释放域的对象。
receiver 必须保留选定 record 指针或 opaque 别名的精确身份。保留地址行为未知、析构协议不支持或所有权证据不足时，不会猜测生成拥有型包装。
详见[Runtime 生命周期](runtime-lifetime.cn.md#显式-c-所有权与成员展示)。

## 常量与原生枚举的呈现定制

`api.constants` 通过稳定来源 ID 或无歧义的原始名称选择已有的原生标量／字符串常量。
`api.nativeEnums` 选择已有的原生枚举及其已有成员。可以修改托管名称、替换安全转义的文档，
以及明确声明托管值；原生事实、枚举成员集合和枚举 ABI 宽度仍保留。
`api.enums` 继续负责另外定义的托管枚举，两者用途独立。

```json
{
  "api": {
    "emitLibraryNameConstant": true,
    "constants": [
      {
        "constant": "NATIVE_VALUE",
        "name": "PresentedValue",
        "managedType": "long",
        "documentation": "明确声明的托管呈现值。",
        "expression": "6L * 7L + 1L"
      }
    ],
    "nativeEnums": [
      {
        "nativeType": "NativeMode",
        "name": "Mode",
        "documentation": "保留原生枚举载体，定制公开名称。",
        "values": [
          {
            "member": "NATIVE_MODE_FIRST",
            "name": "First",
            "value": {
              "kind": "integer",
              "value": "11"
            }
          }
        ]
      }
    ]
  }
}
```

同一个成员的类型化 `value` 和 `expression` 只能选一个。类型化值保留准确整数文本和
Unicode 字符串。表达式必须是有大小限制、可使用框架、生成常量／枚举载体，以及显式声明的
`api.projectionInputs.sources` / `api.projectionInputs.references` 求值的 C# 编译期表达式。
语句、预处理指令、运行时调用、未声明的消费者专有引用、枚举越界值及有歧义的
选择器会在输出前失败。规划器以 warnings-as-errors 编译断言，读取编译器常量值并转换为确定的
字面量；最终产物复核拒绝常量的类型、值和可见性变化。完整消费者构建仍会验证其全部引用和源码。

Host 在 emission 缓存查询前冻结每个声明的源码、程序集引用、框架元数据，以及完整的相邻依赖闭包；
没有被选中常量使用的依赖也会记录。显式引用优先，相邻查找依次检查引用方程序集目录和配置中
声明的引用目录。之前缺失的更高优先级候选同样被追踪，新增同名 DLL 会使旧结果失效。
程序集身份、原始字节和实际预处理符号进入缓存身份；生成期间变化则拒绝正式发布。
元数据分析不加载消费者程序集、不执行其初始化器。验证收据保存路径和内容摘要，不嵌入 PE 镜像。

未使用的框架兼容 facade 可能把可选类型转发到当前框架之外的程序集。只有纯类型转发、
没有实现或类型依赖的框架目标可以缺省；一旦选中缺失的转发类型，常量验证仍会失败。
消费者程序集始终要求完整的引用闭包，包括未选中的成员和纯类型转发依赖。

例如把包含 `Vendor.Values.Answer` 的源码或程序集加入 `api.projectionInputs`，再声明
`"expression": "global::Vendor.Values.Answer"`。`api.projectionInputs.defines` 在静态 `net10.0`
框架符号上追加；`DEBUG` 等符号必须显式提供。选中常量及其字段、签名、别名、依赖中的错误会使生成失败。
只有生成 API／上下文的手写 partial 方法体中，与常量求值无关的错误可以延后，因为常量求值无法执行
这些方法体。消费者构建仍需编译全部源码与方法体；静态绑定不是完整消费者构建证明。

这些覆盖单独保留 `UserAsserted` 来源，不会提升为已经验证的原生事实。例如把原生 `42`
呈现为托管 `43` 后，实际原生函数仍返回 `42`。修改枚举项数值会改变使用该托管名称传参时的数值，
因此需要按实际 API 意图声明；枚举底层载体及 raw 导入签名始终保持。

默认在实例上下文，以及静态风格对应的共享 facade 上提供公开的 `const string NativeLibraryName`。
三种 import mode 都支持，在加载模块前即可读取。设置 `api.emitLibraryNameConstant = false`
即可省略；只有启用时才保留这个名称。产物处理器不能悄悄替换配置的库名值。

如需由消费者声明导入库名常量，设置
`api.importLibraryNameConstant = "Vendor.LibraryNames.Native"`，并通过
`api.projectionInputs.sources` 或 `api.projectionInputs.references` 声明其源码或程序集。
DllImport、LibraryImport 的属性保留完全限定的字段引用。生成时只编译冻结的输入，
不执行消费者代码；字段必须是 `const string`，值必须精确等于最终导入名
（浏览器静态模块为 `__Internal`）。字段缺失、可变或值不符都会阻止发布。
字段所属类型必须位于生成 namespace 之外，处理器不能插入遮蔽类型或修改引用。
此设置与信息性的 `NativeLibraryName` 常量独立。FunctionTable 使用 module provider
或 `Load` 参数，因此拒绝这个仅适用于静态导入的设置。常量源码或程序集变化会使缓存失效，
并接受冻结输入的复核。

## 类型作用域与有限输入重载

`api.nestGeneratedTypesInApi` 默认为 `false`。启用后，原生存储结构、数组载体、原生和
额外托管枚举、具名 ABI delegate 声明都位于公开 API 类型中，例如 `WidgetApi.Packet`。
静态风格使用公开的 `WidgetApi.Packet`，其独立生命周期上下文仍是 `WidgetApiContext`。
分文件与单文件使用相同的类型身份。嵌套不改变原生布局、字段偏移、调用约定或模块生命周期；
与 API 方法、属性和辅助类型冲突的名称在生成文件前失败。

同一个原生函数可以声明有限的额外托管输入形态。例如已有 `int add(int left, int right)`：

```json
{
  "api": {
    "nestGeneratedTypesInApi": true,
    "functions": [
      {
        "function": "add",
        "inputVariations": [
          {
            "parameters": [
              {
                "parameter": "left",
                "projection": {
                  "managedType": "long",
                  "managedToNativeExpression": "checked((int){value})"
                }
              }
            ]
          }
        ]
      }
    ]
  }
}
```

这会保留原有 `Add(int, int)`，并增加 `Add(long, int)`；转换溢出时不会调用原生函数。
variation 的可选 `name` 可选择另一方法名。每个转换目标是该参数已有的 friendly 类型，
例如先把 `Vector3` 转成已有的 `Vector2`，再进入原有原生标量转换。
不复制 raw imports，也不另写一套固定、错误处理、析构或释放逻辑；实例与共享静态 facade
调用相同的既有操作。明确的 `in/ref/out` 借用不能替换为这种值转换，避免破坏地址别名语义。
隐藏参数、有歧义的基础 friendly 形态和重复 C# 签名都会失败。默认参数重载与 input variations
共同受 `api.defaultOverloadLimit` 约束。转换表达式是用户声明的代码，最终消费构建仍需验证
类型及引用；它们不构成已验证的原生语义证据。

## 独立类别筛选与有序名称替换

`source.selection.categories` 为每类声明提供独立 allowlist。例如只公开两个函数，
同时保留所有枚举并只公开一个结构：

```json
{
  "source": {
    "selection": {
      "categories": [
        {
          "category": "callables",
          "includeDeclarations": [
            "read",
            "write"
          ]
        },
        {
          "category": "records",
          "includeDeclarations": [
            "Packet"
          ]
        }
      ]
    }
  },
  "api": {
    "naming": {
      "replacements": [
        {
          "match": "Http",
          "replacement": "HTTP",
          "ignoreCase": true
        },
        {
          "match": "Internal",
          "replacement": ""
        }
      ]
    }
  }
}
```

未列出的类别不受独立 allowlist 限制；全局 `includeDeclarations`、header 和类别筛选
仍同时生效。每个类别最多一条规则，选择器必须属于该类别，歧义重载需要精确签名或稳定 ID。
类别 `includeDeclarations` 只筛选普通公开根，所选函数需要的类型和明确的释放函数仍被保留。
`excludeDeclarations` 是严格排除；若排除了必要存储或释放入口，则报告依赖链并失败，
不生成残缺 ABI。显式模板、shim 等适配请求可以绕过正向筛选，不能绕过排除。

类别选择器在该类别内解析名称。对于常见的 `typedef struct Item { int value; } Item;`，
tag 和 typedef 不会让 `records` 中的 `Item` 产生歧义。未显式选择的同名 typedef 随 tag 的类别筛选；
显式 alias 请求及不同名称的 alias 仍保留独立的必要存储契约，不能借此绕过已排除的 tag。

名称替换采用配置数组的顺序，作用于类别大小写、前后缀处理后的名称，并先于保留名称保护。
默认按 ordinal 区分大小写，`ignoreCase` 使用 ordinal 忽略大小写；不读取当前区域设置。
替换是字面子串操作，不执行正则表达式，也不循环到不再匹配。显式 `api.names` 映射优先，
不再被全局命名规则重写。替换后的空名称、无效 C# 名称或签名冲突都会失败，原生导出名和布局不变。

## 显式原生字符串返回值

编码和长度协议与所有权分开声明：

```json
{
  "semantics": {
    "ownedResults": [
      {
        "function": "get_text",
        "releaseFunction": "free_text",
        "releaseDomain": "library-text",
        "releaseThreadDomain": "any"
      }
    ],
    "stringResults": [
      {
        "function": "get_text",
        "encoding": "utf8",
        "zeroTerminated": true,
        "allowsNull": false
      }
    ]
  }
}
```

只有真实原生返回值是宽度匹配的整数 code-unit 指针、释放入口满足精确同步 `void(T*)` ABI 时，
才生成复制后的托管 `string`。释放域与任意线程释放保证保持 `UserAsserted`。解码、长度校验和释放
位于同一个有效 call lease 中；即使解码失败，`finally` 仍释放非空的 owned 结果。
`allowsNull: true` 返回可空 `string?`；`false` 拒绝 null。不会根据 `char*` 或函数名推测编码。

UTF-16、UTF-32 分别使用 `utf16`、`utf32`；`byteOrder` 支持默认 `native`、`littleEndian`、`bigEndian`。
可选 `codeUnitWidth` 同时与编码及原生 pointee 的真实宽度比较。借用的静态缓冲区改用
`borrowedResults: [{ "function": "get_text", "lifetime": "module" }]`，必须保证地址直到模块实际卸载前
始终有效。`call` 指针在 getter 返回时已过期；其他对象的 `owner` 不能被这个复制包装自动延长。

在 `zeroTerminated: true`、非负 `fixedLength`、`lengthParameter` 中明确选择一种长度来源。
精确长度不包含尾部终止符，并保留内嵌 NUL。`lengthUnit` 为默认 `codeUnits` 或 `bytes`，字节长度
必须整除 code-unit 宽度。标量输入长度使用 `lengthDirection: "input"`；私有输出计数使用
`lengthDirection: "output"`，并明确 `lengthOutputCallBounded: true`，保证同步写入且不保留计数地址。
生成签名只隐藏该输出计数；负数、溢出和超过上限的长度在复制前失败。

`maximumCodeUnits` 默认 1,048,576，扫描时包含终止符。它是扫描／复制上限，不能证明整个上限范围
已分配或可读。零终止契约必须保证内存至少有效到第一个零 code unit；逐单元扫描到这里立即停止，
严格解码只读取实际长度。合法更大字符串可显式提高上限。缺少所有权、null、长度依据，释放签名不匹配，
或尚未支持的 status／async／callback 组合保留 raw ABI 并给出省略原因。

## 额外托管枚举的文档

`api.enums` 声明独立于原生 raw 存储的额外托管枚举。枚举与每个值都可以提供可选的纯文本
`documentation`：

```json
{
  "api": {
    "enums": [
      {
        "name": "Status",
        "underlyingType": "uInt32",
        "documentation": "应用状态值。",
        "values": [
          {
            "name": "Ready",
            "value": "1",
            "documentation": "操作已经就绪。"
          }
        ]
      }
    ]
  }
}
```

文本被转义为 XML summary。XML 标签、`//` 标记及看似源码的行均保持为数据；文档字段
不接收源码语句或属性字符串。省略文档时不产生注释，除非启用 `api.emitPlaceholderComments`。
枚举及其成员采用相同规则，嵌套类型、分文件与单文件也一致。文档不改变明确选择的整数载体、
数值或 Flags 契约；声明一个托管枚举也不会自动替换原生 ABI 类型。

## 非拥有型地址上的显式成员

`api.pointerFunctions[].receiver` 选择已在该操作 `parameters` 中映射的一个参数，为对应地址载体增加实例方法。调用时显式提供 API 上下文，例如 `address.Read(api, "AB")`；工厂仍属于 API 上下文。地址自身只保存指针，成员转发到所给上下文的已检查操作，保留字符串转换、`ref`/`out`、临时资源清理和模块调用租约。空上下文或已关闭的上下文在调用 native 前失败。

在 `api.pointerCarriers` 中选择 `NodeAddress` 后，可配置 `{"function":"read_node","name":"Read","receiver":"self","parameters":[{"parameter":"self","carrier":"NodeAddress"}]}`。`self` 可以位于原生参数列表中的任意位置；Raw 签名和上下文方法仍然存在。该选项不能替代拥有型或 Module 借用包装、移除 const，也不能覆盖地址载体的必要成员。调用者必须按原生协议保证指向的分配持续有效；增加实例方法不会取得所有权或延长内存生命周期。

## 带长度的字符串输入

明确声明编码、长度单位、Call 生命周期与不保留指针后，`void set_name(const char* data, size_t length)` 可以生成 `api.SetName(string)`。设置 `zeroTerminated: false`、`lengthParameter: "length"`、`lengthDirection: "input"`、正数 `maximumCodeUnits`，并明确是否允许 null。`lengthUnit` 使用 `codeUnits` 或 `bytes`；长度在严格编码后计算，分别遵循 UTF-16 码元、UTF-32 码元或 UTF-8 字节，不能直接使用 .NET string.Length。内嵌零码元保持完整。允许 null 时，空引用传递空地址与零长度；空字符串传递非空地址与零长度。

便利方法只隐藏独立选择的整数长度参数；长度无法放入原生参数类型时，在调用 native 前通过 checked 转换拒绝。临时文本使用所选分配器及匹配清理入口。Raw 继续提供精确指针与长度。此转换不支持固定长度、保留文本、输出长度或与其他文本/buffer 共享长度；这些情况需要独立协议。零终止输入继续保留既有截断保护，拒绝内嵌 NUL。

## C++ 文本值返回

C++ 文本值可以在创建它的同一次调用结束前复制为托管 `string`。该包装需要全新拥有的 holder、精确计数与有界批量复制导出、明确字符串编码，以及仅允许在创建调用内清理的能力。使用默认 `char_traits` 和 `allocator` 的精确标准 `basic_string`，以及 `filesystem::path`，可以取得由编译器检查的有限清理事实。它仅允许在创建时的调用线程同步销毁，不授予逃逸 handle、任意线程或 finalizer 清理权限。自定义 traits、allocator 与 family 不能凭熟悉的名字或 `noexcept` 自动取得这一能力。

在 `bridge.cpp.familyBindings[].nativeType` 中使用 `std::string`、`std::u16string`、`std::u32string` 等精确命名别名，并分别声明 `stringEncoding`。选择器仍然解析真实编译器模板参数，并拒绝冲突绑定；`char` 存储不会自动变成 UTF-8。路径遵循所选 `pathFormat`，需要保留本地分隔符时可选择 `nativeUtf8`。`api.maximumValueStringCodeUnits` 默认 1,048,576，限制完整复制的码元数量。编码或清理事实未知时，保留精确 Raw holder 操作并报告包装省略原因。

文本返回方法在工厂调用、长度验证、一次批量复制、严格解码和匹配销毁的整个过程保留一个资源 lease 与一个调用 lease。空文本仍为 `string.Empty`，内嵌零码元完整保留。长度/复制结果不一致、Unicode 非法或超出上限时仍执行匹配清理；意外销毁失败会保留模块并让卸载失败，不重试结果不明确的释放。复制和清理同时失败时，两项错误共同保留。

## 可选的完整 Runtime 源码

源码模式默认仅输出生成 API 所需的最小依赖闭包。需要在手写消费者中使用全部公共 Runtime 工具时，可以显式配置：

```json
"runtime": {
  "mode": "source",
  "sourceFeatures": ["runtime.complete"]
}
```

这会包含模块、句柄、回调、异步、编解码、分配器、Bool8/Bool32、Pointer/ConstPointer、位字段、特殊数值存储与底层互操作工具，不增加生成器或 Runtime 包引用。分文件或单文件遵循普通输出配置。`sourceFeatures` 区分大小写、不允许重复，且仅用于源码模式。SDK 可通过 `RuntimeSourceCatalog.GetFeatureIds()` 查询精确的单独特性，通过 `EmissionRequest.additionalRuntimeSourceFeatures` 直接请求 backend 源码。配置、源码目录实现身份和选择的特性都参与生成缓存。

包含实现不等于授予未支持的调用 ABI 或运行能力。特殊数值载体仍然只是存储形式，动态模块加载及动态 delegate 仍受执行环境限制。完整源码消费者与 Runtime 包具有相同的公共 Runtime 类型和成员。每个库可采用自己的源码 namespace，不共享可变模块状态。
