# C++ 扩展实战手册

[English](cpp-extension-cookbook.md) | [Lowering 契约](lowering.cn.md)

完整 SDK 和工具读取同一份分组 `bindgen.json`。有限的类型转换使用 recipe；基于中立声明的规划使用仅依赖 Contracts 的插件；必须由项目 C++ 解释的实现使用显式 C shim。所有入口都参与 Host 的 worker 解析、ABI 复核、缓存、候选验证和发布。

| 需求 | 扩展 |
| --- | --- |
| 固定 C 载体与转换表达式 | `cpp.typeLowerings` |
| 一个原生参数展开成多个 C 输入 | `abiParameters` 与类型化 argument group |
| 排除、命名、包装普通调用 | `cpp.callableLowerings` |
| 根据中立声明规划类型和调用 | Contracts-only lowering 插件 |
| 通过准确的 C 导出暴露私有实现 | `cpp.nativeShims` |
| 生成具体模板实例 | `cpp.templateInstantiations` |
| 生成原始虚接口回调适配 | `cpp.virtualCallbacks` |
| 保留原始导出并增加检查过的标量元素序列输入 | `cpp.sequenceInputAdapters` |
| 对准确的自定义模板族复用有限容器协议 | `cpp.familyBindings` |

## 准确匹配自定义容器模板族

`cpp.familyBindings` 可以让自定义模板族选择有限的 `string`、`vector`、`span`、
`array`、`optional`、`map`、`set`、`expected`、`uniquePointer`、`sharedPointer`、
`variant`、`path`、`duration` 或 `timePoint` 协议。使用准确的限定模板名称，不带模板参数或通配符：

```json
{
  "cpp": {
    "familyBindings": [
      {
        "nativeTemplate": "Engine::Vector",
        "family": "vector",
        "evidence": {
          "level": "userAsserted",
          "origin": "已根据 Engine Vector 的公开实现检查该协议"
        }
      },
      {
        "nativeTemplate": "Engine::Text",
        "family": "string",
        "stringEncoding": "utf8",
        "evidence": {
          "level": "userAsserted",
          "origin": "Engine Text 保存 UTF-8 code unit"
        }
      }
    ]
  }
}
```

原生对象在 opaque holder 中仍使用它实际的 C++ 类型，不会被重解释为 STL 容器。
被选择的声明图必须含有完整的具体模板实例，以及编译器提供的元素和 extent 事实。
重复的准确绑定、与已知标准库族冲突的绑定、同类型同时选择 recipe，以及缺失具体模板
事实都会明确失败。

原生编译器复核实际使用的有限操作：准确的 `value_type` 或 `element_type`、无符号
整数 count、元素访问和赋值、所需 `push_back`/`reserve`、optional presence，以及
借用 span 的指针身份。固定 extent 在访问或构造前另做运行时检查；optional 的值访问
先检查是否有值，再调用可能不检查状态的自定义 `value()`。证明 token 或原生类型
标识符的宏不能悄悄改写这些检查。

Map 绑定要求准确的 `key_type`/`mapped_type`，const `begin()` 与 `end()` 返回同一
iterator 类型，并复核布尔相等比较、递增、准确的 key/value 访问和 `insert_or_assign`。
Set 绑定要求准确的 `value_type`、相应 iterator 协议和 `insert`。两者都检查下标范围，
并在递增和解引用前检查 iterator 是否已到结尾；即使声明的 count 超过真实遍历范围，
也会进入原生错误边界。遍历顺序由原生容器决定，并发修改仍须由调用者同步，不推定
连续存储或 `Span` 生命周期。

Expected 绑定要求准确的 `value_type`/`error_type`、布尔 `has_value()` 和准确的 const
value/error 访问。成功值为 void 时不虚构值 getter；进入成员访问前先检查当前状态，
失败采用既有的 status/error 清理协议。例如，可以按上面的证据格式，把
`Engine::Result` 显式绑定为 `family: "expected"`。形状检查不把相似类型变成 STL
实现，也不证明其任意行为；相关声明仍为 `UserAsserted`。

Unique/shared 绑定要求准确的 `element_type` 与 `get()` 指针、布尔 presence 成员、
默认构造和不抛异常的析构。Unique holder 必须不可复制并可不抛异常地移动；shared
holder 必须可不抛异常地复制。Unique 按值参数从源 holder 移走所有权，shared 按值
参数复制原生 holder。`get()` 地址仍借用自 holder。这些检查不证明任意线程均可清理，
也不证明任意实现的所有权行为；相关行为声明仍是 `UserAsserted`。

自定义 variant 另需显式配置 `"variantAccess": "memberGet"`。Const `get<I>()`
必须返回准确的 alternative，`index()` 必须返回有界无符号整数。生成代码先检查当前
分支，再调用成员；不会假设 `std::get` 适用于自定义类型。

Path 另需显式配置 `"pathFormat": "genericUtf8"`。`generic_u8string()` 必须返回
可按 count 访问、code unit 为一字节整数的结果；编码与 separator 行为由用户明确
声明。UTF-16 结果不能通过一字节协议，不会猜测平台 `wchar_t` 的编码。

Duration 必须有具体的 representation 与正值有界 `std::ratio` 模板事实。原生编译
检查公开的 `rep`、`period`、规范化分子分母和准确的 `count()` 结果。Time-point
还需已建立的 duration 族，以及准确的 `clock`、`duration` 和 `time_since_epoch()`。
Count 保留原生表示与有理数单位，不悄悄换算成纳秒，也不虚构兼容托管日期的 epoch。

无关的族不能设置 `variantAccess` 或 `pathFormat`。非模板 String/Path 的准确类型
选择需要单独协议，不属于模板名称匹配的范围。

这些检查验证有限操作的形状；任意分配、所有权、线程、编码和方法行为仍是明确的
`UserAsserted` 声明。绑定模板族不能证明借用元素比 owner 活得更久，也不能证明任意
线程均可析构。自定义 bool vector 不会仅因为选择 vector 协议就被认定为 packed 存储。

序列输入重载仍需配置 `cpp.sequenceInputAdapters`；借用 span 仍需显式声明 call
生命周期和不保留。String 族必须声明与编译器 code-unit 宽度匹配的 UTF 编码。不同成员
协议和额外所有权语义使用类型化 recipe、插件或 native shim。
`generate` 后的 native build 和实际调用仍待执行，支持程度以验证收据为准。

## 原生重载名称与选择器

普通 C++ 调用使用原方法名，构造命名为 `Create<Type>`，析构命名为 `Destroy<Type>`，有限的 operator 使用 `Add`、`ToInt` 等名称。生成前按完整 C# 参数签名检查 raw 与 friendly 重载；返回类型与可空引用注解不能区分重载。

重载选择器包含限定名称、准确源参数类型与成员限定符，例如 `Demo::Counter::Counter(int)` 或 `Demo::Counter::Evaluate(double) const`。`inspect` 输出 `sourceSignature`，复杂模板与指针声明可以直接复制。名称映射按准确 C 导出、准确源签名、原限定名称依次匹配。语义选择器匹配多个函数时失败，必须明确源签名。

## 准确字符串与原生路径协议

非模板类或其别名，以及 `std::u16string` 这类标准 `basic_string` 的准确命名别名，
使用 `nativeType`，与 `nativeTemplate` 互斥。原生 canonical
类型身份保持不变，同一类的两个别名不能配置冲突协议。默认 Indexed String
需要公开的编译器 `value_type`、count、索引、append 与 reserve 成员。
只有 `c_str()` 的类可以选择 `stringAccess: "cStrNullTerminated"`：

```json
{
  "cpp": {
    "familyBindings": [{
      "nativeType": "Engine::Text",
      "family": "string",
      "stringEncoding": "utf8",
      "stringAccess": "cStrNullTerminated",
      "evidence": { "level": "userAsserted", "origin": "已审核 Engine Text 协议" },
      "cStr": {
        "maximumCodeUnits": 1048576,
        "readableLifetime": {
          "kind": "call",
          "evidence": { "level": "userAsserted", "origin": "单次访问调用内可读" }
        },
        "nullTerminated": { "level": "userAsserted", "origin": "声明的可读上限内有终止符" },
        "constructorCopiesInput": { "level": "userAsserted", "origin": "Text(const char*) 返回前复制输入" }
      }
    }, {
      "nativeType": "Engine::Path",
      "family": "path",
      "pathFormat": "nativeUtf8",
      "evidence": { "level": "userAsserted", "origin": "u8string 提供带原生分隔符的计数 UTF-8" }
    }],
    "sequenceInputAdapters": [{
      "function": "Engine::CheckText",
      "parameters": [{ "parameter": "value" }]
    }]
  }
}
```

元素身份来自准确、可访问、无参数的 `c_str()` 返回指针，不要求 `value_type`、
`size`、索引或变更成员。Count/GetAt 有界扫描；空指针、上限内没有终止符和越界
索引返回原生错误。上限包含终止符，本身不能证明任意内存可读。编码、可读生命周期
和终止符仍是明确的用户断言，receiver constness 保持实际 getter 的事实。

可选的复制构造断言允许显式请求返回标量或 void 的 sequence-input adapter。
适配器从计数输入复制出带终止符的临时存储，拒绝内嵌零以及达到扫描上限的长度，
再在该次调用内构造真正的原生类。不得保留临时存储地址；const-reference 输入
还需普通的 call/nonretention 断言。data/length 文本契约会保留，但原生 helper
测试通过不等于 C# string 便捷重载已经实现并验收。

`nativeUtf8` 使用 `u8string().data()/size()`，保留原生分隔符；`genericUtf8`
使用 `generic_u8string().data()/size()`。返回的计数对象只需 data/size，不必有
`value_type` 或索引成员。实际原生编译检查有界无符号 count 和一字节整数单元。
这两个选择不会推断任意自定义 path 构造函数，也不把 Windows 窄字符串当成 UTF-8。

计数容器和最小协议的 fixture、收据分别记录在
`eng/acceptance/cpp-neutral-parity.json`。本地原生阶段不能替代发行包、其他操作
系统、NativeAOT 或浏览器执行的认证。

## 标准序列输入适配

对于 `int Demo::SumVector(std::vector<int> values)` 和 `int Demo::SumSpan(std::span<int> values)`，添加：

```json
{
  "cpp": {
    "sequenceInputAdapters": [
      { "function": "Demo::SumVector", "parameters": [{ "parameter": "values" }] },
      { "function": "Demo::SumSpan", "parameters": [{
        "parameter": "values", "retainsArgument": false, "lifetime": "call"
      }] }
    ]
  }
}
```

原来的 opaque 导出继续存在。新增导出接收元素指针与 64 位无符号元素数量，通过 `inputAdapterOf` 关联原导出。默认托管名称为 `SumVectorWithSequence` 或 `SumSpanWithSequence`，可以按准确 adapter 导出名称覆盖。源签名和限定名称语义选择器继续选择原导出，准确导出选择器才能明确选中 adapter。

`basic_string`、`vector`、`array` 逐个转换并复制标量或 enum 元素到临时原生值。`vector<bool>` 规范化每个元素，不借用压缩位存储。普通字符串以 code-unit span 输入，不推断 UTF 编码。数组和固定 span 检查编译器给出的 extent；非空数量配 null 指针、超过 native `size_t` 的数量在原生异常边界失败。

`span` 借用具有相同原生元素类型的存储。借用 span 或 const-reference 临时值要求明确 `retainsArgument: false` 与 `lifetime: "call"`，证据仍是用户断言。可写 span 映射为 `Span<T>`，const 元素映射为 `ReadOnlySpan<T>`。bool、enum 和 C++ 独立宽字符类型不能借用规范化载体。原生编译另行检查借用元素的类型身份。

当前有限适配支持返回标量或 void 的普通调用。构造、自定义调用/结果表达式和 pointer/reference/record 返回需要额外的参数逃逸协议。可变或 rvalue 对象引用保留 raw。拥有型对象方法在同步 handle 调用外固定 span，只在 lambda 内捕获整数地址与检查后的长度；固定作用域覆盖完整调用，不捕获 ref struct。

## 单值 recipe

以下片段放入声明了 `Engine::EntityId` 的完整配置：

```json
{
  "cpp": {
    "typeLowerings": [{
      "id": "engine.entity-id",
      "typePattern": "Engine::EntityId",
      "priority": 100,
      "cAbiType": "uint64_t",
      "parameterExpression": "Engine::EntityId::FromRaw({value})",
      "resultExpression": "({value}).Raw()",
      "managedProjection": {
        "managedType": "EntityId",
        "managedToNativeExpression": "{value}.Value",
        "nativeToManagedExpression": "new EntityId({value})",
        "requiredNamespace": "Engine.Managed"
      }
    }]
  }
}
```

消费项目需要提供 `Engine.Managed.EntityId`，并把源码或引用纳入候选编译验证。raw 导入保留实际 C 类型。即使类型规则没有匹配任何声明，所有配置中的托管类型和表达式也要通过语法验证。

原生输入占位符为 `{value}`、`{nativeType}`，输出占位符为 `{value}`、`{resultType}`。原生结果先保存到局部变量，因此转换不会重复执行原始函数。匹配忽略空白，使用 ordinal 规则和 `*`；同优先级的独占匹配冲突会失败。

`size_t`、`uintptr_t`、`ptrdiff_t`、`intptr_t` 按实际解析目标的指针宽度转换。指针载体本身不能证明所有权、保留行为、分配器或编码。

## 多个 ABI 输入

原生 `Range` 可以由地址和长度构造：

```json
{
  "cpp": {
    "typeLowerings": [{
      "id": "engine.range",
      "typePattern": "Engine::Range",
      "cAbiType": "uint64_t",
      "abiParameters": [
        { "name": "data", "cAbiType": "const int32_t*" },
        { "name": "length", "cAbiType": "size_t" }
      ],
      "parameterExpression": "{nativeType}({data}, {length})"
    }]
  }
}
```

`cAbiType` 仍表示结果的主载体，`abiParameters` 表示输入展开。名称以字母开头，只包含字母与数字，在当前参数组内唯一；`nativeType` 为保留名称。支持固定标量和一层指针。

规划器生成 `BridgeArgumentGroup`，记录准确的原生参数 ID、类型 ID、C 参数 ID 与转换表达式。验证要求原生参数顺序一致、C 输入全部覆盖且不重复。调用中的普通参数保留单载体组。

输入展开必须声明具有内容指纹的原生表达式。单值 managed 输入表达式无法表达多载体固定和生命周期；友好缓冲区 API 需要类型化 managed projection 和明确语义。raw API 保留实际展开签名。

## Recipe 的拥有型结果

明确分配的非 const 指针结果可以在 type recipe 中声明 `ownedResultRelease`，字段为 `nativeFunction`、`allocatorDomain`、`releaseThreadDomain`。释放名称必须对应真实的原生声明，不能填写任意 cleanup 表达式。原函数返回 native `void`，生成的类型化释放导出只有匹配的 C 指针输入；规划器会纳入普通入口选择没有包含的释放依赖。

线程域默认为 `unknown`；只有明确断言清理与线程无关时才填写 `any`。最终 `BridgeResultOwnership` 保存准确导出 ID 与分配域。配置证据保持 `UserAsserted`，拥有型包装通过模块 lease 保留清理能力。缺失或不匹配的释放在发布前失败，拥有型结果不能同时声明无关的单值 managed 输出表达式。

## Callable recipe

```json
{
  "cpp": {
    "callableLowerings": [
      {
        "id": "engine.checked-add",
        "functionPattern": "Engine::Math::Add",
        "priority": 200,
        "exportName": "engine_math_checked_add",
        "invocationExpression": "Engine::Interop::Check({invocation})"
      },
      {
        "id": "engine.hide-debug",
        "functionPattern": "Engine::*::Debug*",
        "exclude": true
      }
    ]
  }
}
```

包装必须包含准确的一次 `{invocation}`。使用的头文件需要已经登记为 source 输入。排除规则不能同时声明 helper、包装或导出名；改名不能改变原函数的类型身份。

## Contracts-only 插件

[examples/LoweringPlugin](../examples/LoweringPlugin/README.md) 包含完整实现。插件实现 `BGCS.Contracts.Plugins.IBindingPlugin`，注册会话级 `ICppCallableLowering` 工厂。服务读取 `NativeDeclaration` 并返回完整 `CppCallableLoweringPlan`，包含 receiver 载体、准确的 native member action、C 导出和类型图。

```bash
cd examples/LoweringPlugin
dotnet build BGCS.Example.LoweringPlugin.csproj --configuration Release
python write-manifest.py
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
```

重编译后重新生成插件 manifest。它记录程序集和依赖 hash；插件与服务的 fingerprint 另外描述私有状态和外部输入。无法完整提供指纹的服务允许执行，但会关闭受其影响的生成缓存。默认每个会话创建独立服务。

原生 helper 使用 `ControlledSourceFragment`，包含准确 UTF-8 SHA-256、来源、依赖、头文件、放置位置和证据。它是显式源码扩展，不是安全沙箱。用户断言保持 `UserAsserted`；解析或编译不会自动把生命周期断言升级为 `Verified`。

## 项目拥有的 C shim

标准入口为 [examples/NativeShim/bindgen.json](../examples/NativeShim/bindgen.json)：

```json
{
  "cpp": {
    "nativeShims": [{
      "id": "widget",
      "publicHeaders": ["shims/widget_c.h"],
      "sourceFiles": ["shims/widget_c.cpp"],
      "exports": ["bgcs_widget_create", "bgcs_widget_destroy", "bgcs_widget_sum"]
    }]
  },
  "nativeBuild": { "mode": "sources" }
}
```

公开头声明固定 C-linkage 签名，实现文件包含上游 C++ 头并执行项目转换。路径相对于声明它的配置文件。Host 解析公开头、检查准确导出，并把实现文件交给普通 NativeBuild provider。

```bash
bindgen-cs bridge examples/NativeShim/bindgen.json
bindgen-cs build examples/NativeShim/bindgen.json
```

`native/metadata/bridge.module.json` 保存类型化操作，`bridge.manifest.json` 保存构建输入，最终 ABI 和 verification 元数据保存复核结果。C 导出本身不能证明释放线程域或 callback quiescence；请求拥有型和长期回调包装前需要明确相应协议。

## 模板和原始虚回调

类型实例放在 `cpp.templateInstantiations.types`，字段为 `id` 和 `nativeType`。函数实例字段为 `id`、`nativeCallable`、`returnType`、`parameterTypes`。worker 产生具体事实，函数 wrapper 根据参数声明使用 `std::forward`。

`cpp.virtualCallbacks` 条目包含 `id` 和 `nativeInterface`。第二次普通 C++ 解析验证 proxy，独立 C 解析复核命名 callback typedef。最终 `InteropModule.virtualProxies` 保留准确的方法名、callback 值通道、factory/user-data 身份、真正调整指针的基类转换和匹配的具体析构入口。原生保留边界与 quiescence 仍然未知。

默认 `releaseThreadDomain: "unknown"` 保留 raw API。只有能明确断言具体 proxy 及其接口基类可以在任意关闭线程上销毁时，才设置 `releaseThreadDomain: "any"`；证据等级保持 `UserAsserted`。它允许生成符合条件、由调用者控制的托管 registration，但仍须提供 stop-and-join，保证**全部原生 proxy 执行已经结束**。仅阻止新调用、等待托管 thunk 计数归零，不能证明 C++ proxy 栈帧已经返回。

原始 callback 地址和 user data 必须存活到 proxy 释放。非零 status 在允许抛异常的 C++ 虚方法内部抛出异常；经 BGCS bridge 进入的调用会捕获它。其他上游线程或调用路径必须自行具有原生异常边界。此 status-failure 策略拒绝 nonthrowing 或异常契约尚不确定的虚方法。

对于 `allowsBlockingDisposal: false` 的运行能力，当前托管 registration 不生成，
保留准确的 raw proxy ABI；现有实现同步等待完整的原生 proxy 栈帧退出。普通 retained
C callback 或 async 的 event-loop 协议不能证明虚 proxy 也能非阻塞退出，这项能力
需要独立实现和真实运行证据。
