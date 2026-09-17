# 架构说明

[Wiki](README.cn.md) | [English](architecture.md)

## 依赖规则

依赖只能向内，Analysis 和 Intermediate 不能反向引用 Facade 或 Emitter：

```text
BGCS.Tool
  -> Facade
  -> Application
  -> Configuration + Analysis
  -> Intermediate
  -> BGCS.Core + BGCS.CppAst

Emission -> Intermediate
Output 是共享基础设施
BGCS.Runtime 独立于所有生成器程序集
```

## 分层职责

### Facade

`CsCodeGenerator` 保留现有源码兼容，`BindingGenerator` 是返回 `BindingGenerationResult` 的简洁入口。Facade 只验证参数并委托，不允许包含 AST 遍历、ABI 推断、源码合并和原生构建逻辑。

### Application

`BindingGenerationPipeline` 负责完整用例顺序：

1. 解析并验证配置；
2. 解析 native 输入；
3. 建立 declaration graph；
4. 分析 ABI、类型、所有权和重载关系；
5. 生成唯一的 `BindingModule`；
6. 在 staging 目录运行 emitter；
7. 按配置编译验证；
8. 原子提交输出；
9. 返回结构化结果。

### Configuration

- `ConfigLoader`：配置 IO 和相对路径。
- `ConfigComposer`：BaseConfig 合成和循环检测。
- `ConfigValidator`：修改输出前进行完整验证。
- `PresetResolver`：提供已知库和 ABI 默认值。

配置类型只能描述意图，不能写 C# 或遍历 Clang AST。

### Analysis

- `DeclarationGraph`：声明及其 ABI 依赖。
- `TypeAnalyzer`：C/C++ 类型降级到 IR。
- `AbiLayoutAnalyzer`：size、alignment、field、array、union、packing 和 bitfield。
- `OwnershipAnalyzer`：borrowed、owned、transferred、caller-allocated 和字符串编码。
- `OverloadPlanner`：pointer/count、capacity/written-count、string、span、callback 和 two-call pattern。

Analyzer 不允许创建文件。

### Intermediate

最终 emitter 只能消费 `BindingModule`、`BindingType`、`BindingFunction` 和 `MarshallingPlan`。IR 包含已解析的名称和 ABI 事实，但不包含 Roslyn syntax、C++ 源码字符串、文件系统路径或可变 Clang 状态。

### Emission

- `CSharpEmitter`：raw import 和友好 API。
- `CBridgeEmitter`：C++ 的 ABI 稳定 wrapper。
- `RuntimeEmitter`：可选 standalone Runtime。
- `SingleFileComposer`：基于语法树的确定性合并。

Emitter 不负责推断 ownership/layout；分析缺失必须报错。

### Output

`OutputDirectoryTransaction` 在目标卷 staging。只有全部 emitter 和 validator 成功后才替换输出，因此失败不会删除 last-good bindings。

## 迁移规则

仓库仍有旧的 AST 直接生成步骤。迁移期间：

- facade 签名保持兼容；
- 每条迁移路径必须先有 IR 测试和生成代码编译测试；
- legacy emitter 与 IR emitter 不得重复定义同一声明；
- 新功能禁止继续塞入 `CsCodeGenerator` God Class；
- 只有 facade 已委托且旧实现被移除，才算完成该层迁移。

## 扩展模型

最终扩展通过强类型接口注册 analyzer、policy、preset 和 emitter。扩展只能取得 immutable request/IR 和 scoped diagnostics，不能依赖 CLI，也不能修改全局 current directory。
