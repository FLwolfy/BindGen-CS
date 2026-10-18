# 仅生成范围的最终收敛路线

[English](roadmap.md) | [架构](architecture.cn.md) | [验收](acceptance.cn.md)

当前计划是十一个产品工程、四产品和一条源码生成流程。旧 facade、Objective-C 产品、
原生构建执行、消费者布局探针、远程配置和 BGCS MSBuild 集成都不属于当前产品。
历史计划保留其原始范围，不是兼容义务。

1. 完成核心功能/配置基线对应及明确 scope removals。
2. 审阅模块归属、耦合、中立端口、生命周期和缓存不变量。
3. 执行当前本地生成器检查及原有各宿主性能预算。
4. 执行 Windows 完整生成器 CI、Linux/macOS smoke 和一个完整产品安装任务。
5. 审阅准确回执与包身份，再按适用流程发布。

AOT/Wasm 应用编译/调用仅为可选手动私有工作，不授权削弱目标事实、源码生成、ABI 检查
或选定 fixture 断言。未知 ownership 和不支持 ABI 仍需拒绝或 shim。

CLI/嵌入式 SDK 使用 `BindingHost`，消费者负责编译、链接、打包、部署输出。插件映射输入/
目标并扩展类型化 lowering 或呈现，不会凭空提供专有 SDK、新 runtime 或未知 ABI 分类器。

源码实现、源码审阅、本地执行、核心验收、公开发布是不同证据状态。提交的状态或过去
成功 revision 不能替代当前基线执行。见[评估](assessment.cn.md)、[测试](testing.cn.md)
和[发布验证](release-verification.cn.md)。
