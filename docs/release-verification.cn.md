# 发布验收

本地通过的测试属于诊断证据。正式发布要求干净的发布 revision、完整发现并执行的测试、实际安装消费的产品包以及对应的 CI 收据。

`eng/acceptance/required-release-gates.json` 定义发布任务和收据策略。发布工作流检查全部必需任务，并拒绝任何已观察到的失败、取消或跳过任务。产品安装收据必须来自同一次成功 main push 所关联的 package 工作流。

`feature-inventory.json` 中的每个已完成功能还必须提供 `implementation`、`requiredHosts` 和 `releaseEvidence`。平台范围确实较窄时，必须说明 `scopeJustification`。例如某个 host 的实际测试映射：

```json
{
  "host": "windows-x64",
  "kind": "test",
  "project": "BGCS.Tests",
  "match": "ExampleTests\\.RoundTrip"
}
```

每个 `requiredHosts` 都需要独立映射。类别映射使用 `kind: "category"`，并引用完整验收报告中的实际类别名称。没有匹配到测试、测试被跳过、类别失败、工作区不干净、复用预构建输出、revision 不一致或源码摘要变化都会阻止认证。`scripts/verify-feature-release-evidence.py` 根据同 revision 的 CI 报告生成逐功能证据；提交一份本地测试收据无法替代这一过程。

配置能力还有独立门禁。`configuration-baseline.json` 固定最初 243 行的 `configurationId`、source/member/type 和顺序。当前清单可以追加新 ID，但不能删除、重排或改变原有身份。每行都需要完整状态、实际实现文件、各 host 的执行证明，以及解释具体断言的 `acceptanceRationale`。配置证明必须使用锚定且转义的完整测试方法，例如 `^BGCS\.Configuration\.Tests\.ExampleTests\.RoundTrip$`，不能用整个通过的测试工程或通用类别替代。确实分别断言多个配置行为的方法可以被相关行共用。`implementationMapping` 只描述源码对应关系，不会自动成为发布验收证据。

隔离的 SDK 和 workload manifests 由 `eng/toolchains` 锁定。CI 安装确切 SDK 并检查 Runtime，在禁止更新 manifest 的前提下安装 workload，然后验证 manifest 字节和所需 packs。原生编译器与 SDK 的事实来自实际发现和构建过程；会持续更新的 runner 标签不能证明原生工具链版本固定。

产品包保留公开 SDK、worker 和独立 TaskHost 的完整执行闭包，并收集它们全部依赖的许可证资料。针对 NuGet 的 `supply-chain` 必须先验证完整、干净且稳定的 `package-verification.json` 及其确切归档集合、摘要和 revision，失败时不产生输出。之后直接读取交付的 nupkg/snupkg，验证其中复制的许可证、nuspec 和执行依赖图，对归档内容逐项计算摘要，生成传递依赖的 SPDX 关系及 SLSA 源输入。Intel macOS 解析器额外记录实际重定位 dylib 的组件归属、链接依赖、源码身份和交付的许可证文本。不会根据文件名猜测许可证。GitHub OIDC 签名属于单独的正式发布动作。

所有产品和技术 RID 包推送完成后，工作流等待各个公共 NuGet 版本索引出现，对比每个归档条目的解压后内容，只允许 NuGet 仓库签名带来的差别，然后在干净环境从公共源消费 SDK、工具、Contracts 和 Runtime。上述验证通过后才发布 GitHub release。公共源检查补充此前完整的安装、原生调用、AOT 和浏览器矩阵，不能替代这些门禁。

完整验收任务使用托管 runner 所支持的六小时时限，仍需要同 revision 的实际耗时收据。如果以后必须拆分测试，须先验证确定性的 class 分片、互不重叠的分区、完整发现清单以及合并后的实际执行完整性，才能用其替代完整测试门禁。
