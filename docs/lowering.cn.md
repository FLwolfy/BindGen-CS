# 最终 C++ Lowering 扩展架构

[English](lowering.md) | [配置指南](configuration-guide.cn.md) | [架构](architecture.cn.md)

需要从目录结构、源码、配置一直运行到 native/C# invocation 的教程，请直接阅读[C++ 扩展实战手册](cpp-extension-cookbook.cn.md)。

BindGen-CS 使用一条统一的 lowering pipeline 处理内置 STL 类型、声明式项目规则、编译插件和显式 C ABI shim。旧的 `ICppTypeAdapter` / `ICppCallableAdapter` contract 已删除；预发布版本不提供兼容层。

## 选择扩展层级

| 层级 | 适用情况 | 是否执行代码 |
| --- | --- | --- |
| 内置 lowering | string、container、smart pointer、path、chrono 等标准语义 | BGCS 内置代码 |
| `TypeLowerings` / `CallableLowerings` | 可用确定性表达式描述的项目类型、参数展开、命名和调用包装 | 仅受控模板 |
| typed lowering plugin | 需要 AST 匹配、target 分支、额外 native/managed artifact 的类型系统 | 执行受信任 .NET plugin |
| `NativeShims` | 编译器私有 ABI、复杂模板、coroutine 或只能由项目 C++ 代码解释的语义 | 编译项目拥有的 C/C++ shim |

## 声明式类型 lowering

```json
{
  "LoweringSafetyPolicy": "AllowUserAsserted",
  "TypeLowerings": [
    {
      "Name": "engine.entity-id",
      "TypePattern": "Engine::EntityId",
      "CAbiType": "uint64_t",
      "Marshalling": "Blittable",
      "Ownership": "Borrowed",
      "ParameterToCppExpression": "Engine::EntityId::FromRaw({value})",
      "ReturnToCExpression": "({value}).Raw()",
      "ManagedProjection": {
        "ManagedType": "EntityId",
        "ManagedToNativeExpression": "{value}.Value",
        "NativeToManagedExpression": "new EntityId({value})"
      },
      "Safety": "UserAsserted"
    }
  ]
}
```

`TypePattern` 支持 `*` glob。转换模板支持 `{value}`、`{name}`、`{count}` 和 `{cppType}`。`AbiParameters` 可以把一个 C++ 参数展开为多个 C ABI 参数。Managed projection 会生成确定性的 `ConfiguredLoweringProjections.g.cs`；更复杂的友好 API 应由 plugin 贡献 `ManagedSource` artifact。

## Callable lowering

`CallableLowerings` 可以匹配 fully-qualified function pattern、重命名/排除 export，并用包含 `{invocation}` 的表达式包装调用。它适用于 error wrapper、context dispatch 或项目级 tracing；不得用它隐藏错误的 ABI 类型。

## Typed lowering plugin

Plugin 通过 `IBindingPluginHost` 注册：

- `ICppTypeLowering`：类型匹配、C ABI shape、参数/返回转换、ownership、allocator 和 managed projection；
- `ICppCallableLowering`：callable 选择、命名和 invocation lowering；
- `ICppArtifactContributor`：确定性贡献 public header、native source、managed source 或 resource。

所有 extension 按 priority 降序、name 序稳定解析；重复 name 失败。实现 `ICacheFingerprintProvider` 后，其状态进入 content-addressed cache key；无法 fingerprint 的扩展会关闭 cache hit。

## 显式 native shim

```json
{
  "LoweringSafetyPolicy": "AllowUserAsserted",
  "NativeShims": [
    {
      "Name": "engine-coroutine",
      "PublicHeaders": ["shims/coroutine_c.h"],
      "SourceFiles": ["shims/coroutine_c.cpp"],
      "Safety": "UserAsserted"
    }
  ]
}
```

BGCS 把文件复制到事务性输出，public header 自动进入 `Classes.h`，source 自动进入 build manifest，header 中使用 `API(...)` 声明的 symbol 自动参与 export inspection，并可继续生成 C# bindings。这是处理无法直接证明的 C++ ABI 的标准 escape hatch，而不是库名特判。

## 安全策略与 bypass

`LoweringSafetyPolicy` 有三个等级：

- `VerifiedOnly`：默认值，只接受 BGCS 已验证 lowering；
- `AllowUserAsserted`：接受项目审查过的 recipe/plugin/shim；
- `AllowUnsafe`：明确 bypass lowering safety gate，仍产生 `BGCS-SAFETY-LOWERING-BYPASS` 审计诊断。

C binding 侧继续使用 `StrictSafetySeverity=Error|SuppressFriendly|Warning`；`StrictSafety=false` 会显式关闭推断的 ownership/lifetime 检查。项目提供的 managed value carrier 使用 `ExternalTypeContracts` 的 `Reject`、`RequireLayoutMatch` 或 `BypassLayoutValidation`，所有被接受的按值 carrier 都写入 IR 并产生 `BGCS-SAFETY-EXTERNAL-TYPE`。Bypass 只允许用户接管风险，不能把语法上无法表达的 ABI 变成有效代码；此时仍须提供 lowering 或 C shim。
