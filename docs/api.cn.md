# C# 生成接口

普通生成工具引用 `BGCS`；`BGCS.Core` 仅包含数据和接口，不负责执行会话。产品使用 .NET 10 / C# 14。

```csharp
using System;
using BGCS;

await using BindingHost generator = BindingGenerator.CreateDefault();
GenerationRequest request = GenerationRequest.FromFile("bindgen.json");
GenerationResult result = await generator.GenerateAsync(request);
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

`FromFile` 只创建描述，不立即读取配置。宿主确定后，在会话启动时读取配置、本地 extends 和 manifest；传给 `GenerateAsync` 的取消令牌同时覆盖读取。代码配置使用 `new GenerationRequest(configuration, absoluteBaseDirectory)`，设置会独立冻结。

## 组合

`BindingGenerator.CreateBuilder()` 包含基础实现。通过 `RegisterFactory<T>(descriptor, context => implementation)` 注册接口。替换直接注册的服务使用 `ReplaceFactory<T>(existingId, descriptor, factory)`，且必须在冻结前完成。端口不同、身份重复、替换目标不存在均失败。

`BindingHost.CreateBuilder()` 属于中立 Engine，不含具体平台实现。高级调用方提供宿主、frontend、原生目标、runtime 和调用 ABI provider；缺失时不会偷偷恢复默认值。

`IBindingPlugin` 可以通过 `AddPlugin<T>()`、`AddPluginFactory(...)` 或显式部署的本地 manifest 组织一组注册。配置加载后不能替换宿主规则。会话工厂不得返回跨会话共享的可变对象。

## 操作

| 操作 | 结果 |
| --- | --- |
| `AnalyzeAsync` | 中立原生事实、依赖与诊断。 |
| `GenerateAsync` | C#、必要原生源码、清单与诊断。 |
| `BridgeAsync` | 复核后的 C/C++ 桥接源码与要求。 |
| `InspectAsync` | 通过同一生成流程查看模型。 |
| `DiffAsync` | 比较候选，不替换正式输出。 |
| `ClearOwnedOutputAsync` | 仅清理已验证归属的生成输出。 |

这些操作均不编译或运行消费项目。`BindingHostBuilder.Build()` 只是创建协调对象。

## 结果与提交状态

`GenerationResult` 提供 `artifacts`、`nativeSources`、本次机器上的 `nativeInputRoots`、`targetModels`、`capabilities`、`cacheStages` 和 `diagnostics`。`success` 要求完整发布且无错误；`published`、`publicationState` 区分提交前失败与产物已提交但基础设施随后清理失败。

原生源码清单记录逻辑输入根和物理导出映射，机器路径单独返回。消费项目将根 ID 映射到自己的冻结输入，见[源码要求](native-source-inputs.cn.md)。

缓存回执只证明相应阶段的复用，不证明整个请求或原生调用已经验证。诊断包含级别、代码、位置以及可用的有界详情。

`DisposeAsync` 拒绝新请求、取消活动工作并等待会话释放。生成 API 在应用中使用另一套 Runtime 生命周期；释放生成器不会释放应用的原生模块。
