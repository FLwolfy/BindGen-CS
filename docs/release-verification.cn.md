# 发布验证

[English](release-verification.md) | [验收](acceptance.cn.md) | [发布](publish.cn.md)

核心功能/配置基线属于强制验收。必需 CI 是 Windows 完整生成器检查、Linux/macOS
生成器 smoke，以及一个完整产品安装任务。原有各宿主性能预算不变。
Linux smoke 执行本轮固定 Browser Mono Wasm/AOT aggregate 与回调回归一次；
其他应用发布矩阵单独记录。目标事实与源码生成仍受核心基线约束。

准确 workflow、选定 case 和回执策略位于 `eng/acceptance/required-release-gates.json`
及当前清单，必须与实际执行对应。源码存在或 `implementationReady` 不能替代可执行回执。
明确删除的职责写入 `scope-removals.json`，不能伪造为通过证据。

回执标明 revision/digest、clean/dirty、宿主、包/工具字节和实际断言。缺少、失败或跳过
必需 case 不能认证选定 suite。历史报告或 dirty 本地子集不能认证另一个不可变 revision。
源码/API 快照改变保留准确审阅范围，不削弱 ABI/ownership 断言。

产品生成器不编译消费者/原生工程、不运行布局探针、不检查原生目标文件；仓库 fixture
和安装检查可执行外部编译/调用作为所需证据。可选私有应用检查仍保留全部选定断言、
真实前提和范围；无法满足前提不能记为通过。

打包检查声明 archive 集合、依赖/resource 闭包、许可证及源码/包身份。
SDK worker 通过标准 NuGet 内容与经过验证的本地首次准备使用，不依赖 BGCS build
targets 或 TaskHost。发布依据接受的干净 revision，并验证真实公开包可用性。
本文不表示发布已经完成或软件包已经公开。
