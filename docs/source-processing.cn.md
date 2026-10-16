# 托管源码处理

`output.processors` 在 C# 发射完成后处理候选产物，随后仍执行最终导入与布局检查、请求的编译和发布。配置只声明数据，不支持反序列化委托或通过类型名执行代码。

```json
{
  "output": {
    "layout": "singleFile",
    "singleFileName": "Bindings.cs",
    "processors": [
      {"id":"application.extra-api","priority":30,"kind":"addSource","path":"Helpers/Api.Part.cs",
       "content":"namespace Example; public partial class Api { public static int Marker() { return 10; } }"},
      {"id":"application.literal","priority":20,"kind":"replaceText","paths":["Helpers/Api.Part.cs"],
       "match":"return 10;","replacement":"return 20;"},
      {"id":"application.regex","priority":10,"kind":"regexReplace","paths":["Helpers/*.cs"],
       "match":"return (?<value>20);","replacement":"return ${value} + 1;","timeoutMilliseconds":1000}
    ]
  }
}
```

额外 partial 声明必须使用实际生成的 namespace 和上下文名称。示例假定实例 API 的 namespace 为 `Example`，名称为 `Api`。priority 越高越先执行，同优先级按处理器 ID 的 ordinal 顺序执行；ID 不能与其他配置操作或已注册扩展重复。

`paths` 相对于单个目标的 `managed/` 根目录，区分大小写。`*`、`?` 匹配路径段中的字符，完整的 `**` 段匹配目录层级。绝对路径、反斜线、路径穿越和空路径段会失败。每个处理器看到前一个操作完成后的候选集合，因此后续规则可以选择刚加入的文件。新增路径不能与已有文件发生大小写冲突。新增 C# 必须能够解析为普通库源码，不允许顶层可执行语句。

文字替换采用 ordinal 规则，`ignoreCase` 可启用 ordinal 忽略大小写。正则采用 invariant 的 .NET 匹配及标准 dollar 替换语法，包括命名组；可显式设置 `multiline`、`singleline`、`explicitCapture`。`timeoutMilliseconds` 同时限制每个文件的匹配与整轮处理时间，范围 1～30000，默认 1000。替换规则默认要求至少匹配一次；允许无匹配时必须显式指定 `requireMatch: false`。声明和候选文本同样受大小上限约束。

新增文件经过候选产物所有者，并记录在 manifest 中。`singleFile` 会把全部托管 C#（包括新增文件和源码 Runtime）重新合并到指定的 `singleFileName`，不会留下单独的 helper C# 文件；split 模式保留新增相对路径。

最终契约检查仍验证实际 raw 导入、转发和原生存储。改变这些契约的处理器会在发布前失败。普通额外 API 方法可以放入 partial 上下文；源码处理不允许擅自改变 ABI 字段或增加第二个 raw 存储声明。`build` 还会把完整候选源码以 warnings-as-errors 编译；任一必需检查失败时保留上一套正式输出。

内置操作为 ID、优先级、路径、内容、匹配选项、超时和无匹配策略生成完整指纹。配置与实现身份也进入缓存键。缓存保存未经处理的发射结果，恢复后再次运行处理器，不会累积上一次的修改。

嵌入式 SDK 可在 `BGCS.CSharp.Processing` 中使用等价的不可变实现 `TextArtifactProcessor`、`RegexArtifactProcessor`、`AdditionalSourceArtifactProcessor`，通过 Host 的 artifact processor 端口注册工厂。高级 SDK 扩展可派生 `SyntaxArtifactProcessor` 处理独立的 Roslyn compilation unit。该基类默认没有缓存指纹，因此禁用受影响的生成缓存；只有能够完整声明产物输入时才实现 `ICacheFingerprintProvider`。仅引用 Contracts 的扩展仍可实现 `IArtifactProcessor`，无需引用 Roslyn。
