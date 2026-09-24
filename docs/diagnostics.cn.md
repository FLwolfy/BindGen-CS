# 诊断指南

[English](diagnostics.md) | [中文文档入口](README.cn.md) | [配置指南](configuration-guide.cn.md)

BindGen-CS 的安全诊断表示 native 声明缺少生成 friendly managed API 所需的语义。优先补充最小配置，不要通过关闭检查或把非平凡类型强行映射成 blittable struct 来消除诊断。

## 建议排查顺序

```bash
bindgen-cs doctor
bindgen-cs validate bindgen.json
bindgen-cs inspect bindgen.json
bindgen-cs inspect bindgen.json --json
bindgen-cs explain BGCS-SAFETY-OWNERSHIP
bindgen-cs explain --json
```

1. `doctor` 确认 compiler、system includes、target triple 和 SDK。
2. `validate` 在写输出前暴露 parser、ABI 和 safety 问题。
3. `inspect` 检查最终 target、type/function 数量和 IR diagnostics。
4. 需要定位 lowering 时使用 `--json` 查看 Binding IR。
5. 使用 `explain` 读取当前版本的稳定诊断目录；省略 code 可以列出全部 descriptor。

## StrictSafety 模式

| `StrictSafetySeverity` | 行为 |
| --- | --- |
| `Warning` | 保留 raw ABI 和当前 friendly API，同时报告风险 |
| `SuppressFriendly`（默认） | 保留 raw ABI，删除受影响函数推断生成的 friendly overload |
| `Error` | 把风险升级为错误，在正式输出 commit 前失败 |

`StrictSafety=false` 只适用于外部流程已经完整审计这些语义的场景。它不会让未知 ownership 变得正确。

## 诊断代码

| 代码 | 原因 | 推荐修复 |
| --- | --- | --- |
| `BGCS-SAFETY-OWNERSHIP` | pointer return 没有 ownership | 设置 `MarshallingMappings.<function>.Return.Ownership`，必要时补 `CleanupFunction` 和 `RequiresCleanup` |
| `BGCS-SAFETY-CALLBACK` | callback retention/unregister lifetime 未声明 | 为 callback 参数增加 marshalling mapping，并由消费层持有或注销 callback |
| `BGCS-SAFETY-LENGTH` | buffer pointer 没有可证明的 length/capacity 关系 | 设置 `LengthParameter`、`CapacityParameter` 和可选 `WrittenCountParameter` |
| `BGCS-SAFETY-ALLOCATOR` | output string 没有 cleanup allocator | 设置 `CleanupFunction`、ownership、encoding 和 cleanup requirement |
| `BGCSCS001` | IR-native C# emitter 无法在不丢语义的前提下表达某个声明 | 实现通用且有测试的 IR lowering，或显式排除该声明；emission 会在写文件前失败 |
| `BGCSCPP-INSTANTIATION` | 发现 primary template，但没有请求 concrete instance | 把真正需要的完整 specialization 加入 `TemplateInstantiations` 或 `FunctionTemplateInstantiations` |
| `BGCSCPP001` | C++ declaration 没有被接受的 lowering | 配置内置 type list、显式 template instance、添加声明式 lowering/typed plugin/显式 C shim，或选择可审计的安全策略 |
| `BGCS-SAFETY-LOWERING-BYPASS` | 显式允许了 `Unsafe` lowering | 保留项目拥有的 ABI、native invocation、allocator 与 lifetime 测试；证据完备后将 lowering 提升为 `UserAsserted` 或 `Verified` |
| `BGCS-SAFETY-EXTERNAL-TYPE` | 项目提供的 managed value carrier 跨越 ABI | 用 `RequireLayoutMatch` 声明匹配的 target size/alignment、改用 pointer，或显式 bypass 并保留 native invocation 测试 |

## 最小 mapping 示例

```json
{
  "StrictSafety": true,
  "StrictSafetySeverity": "Error",
  "MarshallingMappings": {
    "library_get_items": {
      "Parameters": {
        "items": {
          "Strategy": "Span",
          "Ownership": "CallerAllocated",
          "CapacityParameter": "capacity",
          "WrittenCountParameter": "count"
        }
      }
    },
    "library_create_name": {
      "Return": {
        "Strategy": "String",
        "Ownership": "Owned",
        "Encoding": "Utf8",
        "CleanupFunction": "library_free_name",
        "RequiresCleanup": true,
        "NullTerminated": true
      }
    }
  }
}
```

## Parser 与 target 问题

- 找不到 header：路径从配置文件目录解析；检查 `EntryFiles`、`IncludeFolders` 和文件名大小写。
- umbrella header 没有输出声明：开启 `IncludeTransitivelyReferencedHeaders`，并确保用户头位于 entry 目录或 `IncludeFolders`。
- 系统类型不匹配：不要复制另一平台的 `Defines`；检查 `TargetPlatform`、`TargetArchitecture`、`TargetAbi`、`TargetTriple` 和 `TargetSysRoot`。
- macOS 找不到标准库/SDK：先运行 `xcrun --show-sdk-path`，再用 `doctor` 确认发现结果；必要时设置 `SDKROOT` 或 `CompilerPath`。
- C++ symbol 无法 P/Invoke：没有 C linkage 的 method/function 应经过 `bridge`，不要把 mangled name 当稳定 ABI。

## 仍然不应做的事

- 不要为了通过编译把未知 C++ class 映射成 `nint` 后假装已经处理 lifetime。
- 不要在生成文件中手改 `[DllImport]`、布局或 cleanup。
- 不要用 `StrictSafety=false` 代替项目级 ownership 文档。
- 不要让 binding 的 target 配置与 native binary 的实际 ABI 不一致。
