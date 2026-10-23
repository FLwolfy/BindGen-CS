# 可迁移的原生源码输入

[English](native-source-inputs.md) | [原生元数据](native-metadata.cn.md) | [C++ 手册](cpp-extension-cookbook.cn.md)

BGCS 负责生成源码。原生编译、链接、导出检查、打包与部署由应用自己的构建系统负责。
原生源码清单描述需要交给该构建系统的文件与要求，不是编译器命令，也不是原生构建完成收据。

## 读取已发布的要求

生成成功后，`GenerationResult.nativeSources` 为每个目标提供类型化的
`NativeSourceManifest`。相同数据发布在
`<target>/metadata/native-source.manifest.json`。重新打开既有输出时使用：

```csharp
var manifests = BindingArtifactManifest.ReadNativeSources(
    outputRoot, libraryId, paths, cancellationToken);
```

该 SDK 方法持有发布锁，验证所有权、完整产物哈希及类型化清单。它不会验证消费者之后
重新映射的外部文件，也不执行编译器。普通 C 解析结果的 `sources` 可以为空：这些声明
描述上游库，并不表示 BGCS 为其生成了新的实现。

| 成员 | 含义 |
| --- | --- |
| `sources`、`headers` | 相对于输出根目录的准确生成产物描述 |
| `inputRoots` | 当前目标清单内的逻辑根 ID |
| `inputs` | 根目录相对文件名、长度、SHA-256 与角色 |
| `includeDirectories` | 保持顺序，并区分 system/framework 的搜索目录 |
| `headerReferences` | 原始入口身份及其可迁移 include 引用 |
| `defines`、`languageStandard`、`languageRequirements` | 声明的源码与 lowering 要求 |
| `requiredExports`、`staticExportTable` | 期望的导出符号及可选静态 adapter 协议 |

只有角色为 `ShimSource` 的输入要求额外的消费者翻译单元。`EntryHeader` 与 `Dependency`
不是待编译文件列表。解析器 builtin 头文件仍有经过验证的 frontend 输入身份，独立于消费者 SDK 要求。

## 将逻辑根映射到冻结输入树

`GenerationResult.nativeInputRoots` 提供本次会话的绝对目录绑定。该属性带有 `JsonIgnore`，
物理工作区路径不会写入可迁移清单或普通结果 JSON。需要准备冻结输入树时，由自己的集成保存
这些绑定；单独一份既有清单不能恢复另一台机器上的原目录。

用户头文件搜索复用 `source.includeDirectories`。只有入口头文件确实需要时，BGCS 才在解析
和快照之前加入默认项目根目录。include 有歧义或依赖超出声明根时，需要显式配置根目录，
不会通过猜测另一套编译器 SDK 来修复。

由调用者提供以 ordinal 比较逻辑 ID 的字典，映射每个根：

- `generated` 指向已经验证的输出根目录。
- user/project 根映射到冻结源码树，并保持原有相对关系和重叠子根。不要把每个根平铺到互不相关
  的目录；不同搜索根可能有意引用同一个头文件。
- SDK 根保持为所选工具链的 SDK，或其经过验证的快照。`sdk-root` 标识依赖位置，不会自动成为
  编译器 include 目录。更换 SDK 需要重新生成或审查。
- 使用前按记录的长度和摘要验证映射后的 `inputs`。发布验证覆盖生成产物，不覆盖之后对外部输入的重新映射。

以下示例按清单选择生成源码与 shim 翻译单元，不扫描输出目录。
`remappedRoots` 由消费者提供，其中 `generated` 必须指向 `outputRoot`；include 顺序保持不变。

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS;
using BGCS.Core.Artifacts;
using BGCS.Core.Hosting;

static (string[] Sources,
    (string Directory, bool IsSystem, bool IsFramework)[] Includes)
    ReadNativeInputs(string outputRoot, string libraryId, string targetId,
        IHostPathService paths,
        IReadOnlyDictionary<string, string> remappedRoots,
        CancellationToken cancellationToken)
{
    NativeSourceManifest manifest = BindingArtifactManifest
        .ReadNativeSources(outputRoot, libraryId, paths, cancellationToken)
        .Single(item => item.targetId == targetId);

    string Resolve(string rootId, string relativePath)
    {
        string root = remappedRoots[rootId];
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("Each remapped root must be absolute.");
        return Path.GetFullPath(relativePath, root);
    }

    string[] generated = manifest.sources
        .Select(file => Resolve("generated", file.relativePath)).ToArray();
    string[] shims = manifest.inputs.Where(file => file.role == "ShimSource")
        .Select(file => Resolve(file.rootId, file.relativePath)).ToArray();
    var includes = manifest.includeDirectories.Select(directory => (
        Directory: Resolve(directory.rootId, directory.relativePath),
        IsSystem: directory.isSystem,
        IsFramework: directory.isFramework)).ToArray();
    return (generated.Concat(shims).ToArray(), includes);
}
```

消费者把这些有序要求转换为自己的编译器、链接器输入，同时遵守宏、语言要求、导出约定及上游库依赖。
示例本身不编译或执行任何代码。

系统目录描述 SDK 要求，并不表示可以统一转换为一串 `-isystem` 参数。消费者应保留自己的编译器
builtin headers 所需的搜索位置，并通过该编译器的 SDK 接入方式选择 C/C++ 头文件。
例如 libc++ 的 `stddef.h` 使用 `include_next`；把已经配置的 C SDK 根重复放到 builtin headers
之前，会改变其行为。BGCS 解析器的 builtin headers 不是消费编译器的资源。

## 分析入口与公开头文件

`.cpp` 可以作为分析输入，但翻译单元未必适合充当公开 include。生成 bridge 应包含公开、
自洽的声明头。实现 `.cpp` 保留在消费者构建中；项目 shim 则通过
`cpp.nativeShims[].sourceFiles` 声明实现，并单独配置 `publicHeaders`。如果 bridge 包含了实现文件，
消费者又独立编译它，就可能产生重复定义。BGCS 不改写用户源码来制造公开接口。

## 相关 3.0.1 修复

依赖模板实参现在保存共享类型引用，不再声称拥有已经属于某个声明的 alias 或参数。
这修复了类模板中两个 `std::pair<iterator, bool>` 重载之类的解析模式，不通过清空 parent、
克隆声明或放宽 AST 所有权检查来绕过问题。它不表示所有依赖模板 API 都可以直接 lowering。

worker 通过错误响应报告的解析失败保留简短的诊断 `message`，并把有界 stderr 上下文放入可选的
`BindingDiagnostic.details`。SDK / JSON 结构化结果可以保存这些细节；普通诊断展示及
`GetDiagnosticSummary()` 仍保持简洁。worker 细节最多保留 64 Ki 字符尾部和截断标记；
没有细节时，JSON 省略该字段。

这些描述对应实现变化。版本是否发布、平台是否通过验收，与源码清单或生成成功是独立状态。
