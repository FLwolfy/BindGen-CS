# Objective-C 声明事实

`BindingHost.AnalyzeAsync` 接受 `SourceLanguage.ObjC`。默认 frontend 在独立 Clang worker 中运行，返回不持有解析器句柄的 `NativeModule`。目标、实际头文件路径和所需 Apple SDK 均属于明确输入。只有自包含声明的头文件可以在没有 Apple SDK 的环境中按 Darwin 目标解析；这只证明解析能力。

`NativeDeclaration` 使用独立的中立记录保存 Objective-C 关系：

- `objectiveCContainer` 区分 interface、protocol 和 category，保存协议声明、泛型参数类型、基接口和 category 的目标接口。
- `objectiveCMethod` 保存完整 selector，以及 class 或 instance 的分派域。
- `objectiveCProperty` 通过 `NativeDeclaration.type` 保存实际值类型，同时保留编译器报告的属性标志和精确 getter/setter 声明与 selector。`copy`、`weak`、`strong` 等仍是源语言事实，不会自动产生托管 ownership 或 ARC 规则。

这些记录只包含稳定标识和不可变数据，不包含 Clang cursor。模型验证检查引用，声明选择保留必需的协议、泛型和 accessor 关系。缺失的属性信息保持未知。

Objective-C 泛型和接口继承不使用 C++ 模板及基类访问语义表示。Block 有独立的原生类型类别，不能当作普通 C 函数指针。`instancetype` 返回值保留编译器建立的实际指针类型图。

生成请求目前接受 C 和 C++。Objective-C 事实不代表已经实现 message-send binding、block 调用、ARC 管理、原生链接或运行时认证。`AnalyzeAsync` 不会为 Objective-C selector 生成 C import。按 Apple 目标解析自包含夹具不等于认证 Apple 平台。
