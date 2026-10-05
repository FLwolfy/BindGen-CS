# CI 宿主与目标边界

[文档入口](README.cn.md) · [架构](architecture.cn.md) · [测试](testing.md)

## Clang 内建头文件与 SDK

`extern/clang-resource/Headers.zip` 是 LLVM 20 系列的内建头文件，嵌入解析器程序集。
它不是宿主 SDK，也不是 macOS 专用资源。目标 SDK、C++ 标准库、ABI 和工具链由目标边界配置。
资源的来源、SHA-256 和许可证见[资源说明](../extern/clang-resource/README.md)。

解析器和 compiler discovery 同时传入 `--sysroot` 与 `-isysroot`，使链接根与头文件根一致。
Darwin 的 SDK 搜索优先读取 `-isysroot`；只提供 `--sysroot` 时，环境或编译器默认 SDK
可能参与头文件搜索。具体语义见 [LLVM Darwin driver](https://github.com/llvm/llvm-project/blob/llvmorg-20.1.8/clang/lib/Driver/ToolChains/Darwin.cpp)。
有显式 C++ 搜索根时使用 `-nostdinc++`，避免 libclang 再注入另一套标准库。
Driver discovery 保留搜索顺序与 framework 类别；parser 在 driver 的 builtin 位置放入自己的
资源头文件，保持 **C++ wrapper → parser builtin → SDK C headers**。
不能把全部 SDK 路径提前改为 `-isystem`，否则 `include_next` 会绕过基础类型定义。
该规则通用于工具链发现，不依赖 macOS 的某个安装路径；framework 根使用 `-iframework`。

## 输入、锁与产物

- 缓存输入扫描保留有效 SDK 目录链接，按解析后的目录去重并阻止祖先循环。
- 跨进程目录租约识别 Windows sharing violation，以及 Linux/macOS 不同的原生 `flock` 忙碌错误。
- 测试 fixture 使用当前构建配置；独立 parser host 一并带上真实原生运行库。
- 验收消费者从 MSBuild 查询 `TargetPath`，不假设 `bin` 的平台目录布局。
- Windows CI 清除开发者命令行的 MSBuild `Platform` 环境变量；工具链变量仍保留。
- Wasm workload 在明确选择 .NET 9 的目录安装，独立于仓库的 SDK 10 打包配置。

## 真实库快照审阅

Linux x64 的候选来自[本次真实 CI](https://github.com/FLwolfy/BindGen-CS/actions/runs/37357633285)。
五个 C 库及 bimg C++ 桥均实际生成并以零警告消费者编译通过。
审阅覆盖 Linux pthread 声明、LP64 原生整数、C enum 底层类型与导出签名。
bimg 的 C header 与 C++ bridge 与已验证的 Windows 输出一致；managed enum 采用目标 ABI 的底层类型。
仅更新该宿主实际产生并审阅的三个 manifest，不改变其他架构的基线。

快照变化仍返回失败并保存候选；CI 不自动接受新输出。
独立 NativeAOT、Wasm 解释执行和 Wasm AOT 消费者包含 56 项真实调用检查，
负向验收要求错误原生返回值确实导致失败。最终状态以对应提交的完整 CI 结果为准。

## 开发规范

仓库开发规范使用普通的 [CONTRIBUTING.md](../CONTRIBUTING.md) 与
[C# 规范](csharp-development-standard.cn.md)。它们记录对所有贡献者适用的架构与可读性要求。
