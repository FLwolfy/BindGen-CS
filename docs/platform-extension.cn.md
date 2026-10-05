# 平台目标扩展与 Solution 组织

[中文 Wiki](README.cn.md) · [架构说明](architecture.cn.md) · [通用开发规范](csharp-development-standard.cn.md)

## 生成器宿主与原生目标

宿主是执行 BGCS 的机器；目标是被解析 header 和生成绑定最终运行的 ABI。
例如生成器在 Windows 运行，仍可以在显式提供正确目标 SDK 的情况下解析另一种目标。
是否能编译、链接、签名和运行对应产物，由可用工具链及实际验收决定。

`BGCS.Core.Targeting` 提供中立请求和描述；`BGCS.CppAst.Targeting` 处理 Clang 及平台 provider。
Binding IR 和 Bridge IR 保存解析所得的布局与 ABI 事实，C# emitter 不依赖消费方引擎的模块清单。
`BGCS.Runtime` 独立管理互操作，不承担应用平台组合。

## 例子：解析 iOS 设备目标

当前内置 `AppleNativeTargetProvider` 区分 macOS、iOS 设备和 iOS 模拟器。
下面使用当前公开 API 解析描述，调用方必须传入真实 SDK 目录：

```csharp
using BGCS.Core.Targeting;
using BGCS.CppAst.Targeting;

internal static class TargetSelection
{
    internal static NativeTargetDescriptor ResolveIosDevice(string sdkRoot)
    {
        var request = new NativeTargetRequest(
            new NativeTargetId("ios-arm64-darwin"),
            new NativeToolchainDescriptor(sysRoot: sdkRoot));
        return new ClangTargetResolver().Resolve(request);
    }
}
```

设备 triple 与模拟器 triple 不同。解析成功只说明 provider 能提供该描述；
本轮工程整理没有执行 iOS 生成、编译或实机调用，不能据此宣称 iOS 全链路通过。

## 尚未内置的目标

1. 实现 `INativeTargetProvider.TryResolve`。不属于该 provider 的 ID 返回 `false`；属于它但输入无效时明确失败。
2. 用 `NativeTargetDescriptor` 提供 target/platform/architecture/ABI ID、triple 和工具链描述。
   SDK/sysroot 与 Clang builtin headers 保持各自归属。
3. 使用 `ClangTargetResolver(IEnumerable<INativeTargetProvider>)` 显式组合 provider；该构造函数使用
   调用方提供的完整集合，不会隐式追加内置 provider。
4. 按现有配置/解析入口传入目标描述，共用 AST 分析、冻结 IR、C 桥与 C# 输出链。
   有现有规则无法表达的 ABI 行为时，在所属解析/分析/interop 边界完善共享规则，不能静默猜测。
5. 使用 BGCS 自有 fixture 验证指针宽度、布局、参数/返回、三种 import mode、回调、ownership、
   缺失符号和失败清理；实际编译、链接、运行后才形成目标支持证据。

新增目标不要求创建 `BGCS.Web`、消费方专用项目或新的 emitter 编排。
复用边界和明确失败规则让改动归属清楚，不保证新 ABI 无需实现任何新规则。

## 当前 Solution 组织

```text
BindGen-CS.sln
  src
    BGCS.Core
    BGCS.CppAst
    BGCS.Intermediate
    BGCS
    BGCS.Cpp2C
    BGCS.Language
    BGCS.Runtime
    BGCS.Tool
  tests
    现有测试项目
    fixtures
      现有进程与插件 fixture
```

Solution 共有 22 个实际项目，八个生产项目直接位于 `src` 分组。
两个 example `.csproj` 继续由各自示例流程构建，不作为空 Solution Folder 占位。
GUID、配置和依赖保持原值；工程文件按通用规范采用两空格缩进、职责空行和可读属性声明。
