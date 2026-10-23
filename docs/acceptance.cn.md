# 测试与验收

BGCS 产品止于生成。以下编译、链接和运行只由仓库测试消费者执行，不进入 SDK、CLI 或官方扩展。

## 四项门禁

| 检查 | 范围 |
| --- | --- |
| Generator baseline — Windows x64 | 核心全量、Host 全量一次、架构/API/文档、CLI/SDK、六个锁定语料和性能。 |
| Generator smoke — Linux x64 | 明确的平台差异、真实 worker/消费、本次固定 Browser Mono Wasm 与 Wasm AOT aggregate/回调回归一次。 |
| Generator smoke — macOS x64 | SDK/标准库、UTF-32、真实消费与性能。 |
| Installed product baseline | 同提交候选包的干净安装、闭包、接口扩展、Runtime-only、源码 Runtime 和重定位。 |

不恢复八分片，不在各作业重复全量矩阵。Windows 执行完整发现集合，其他宿主执行明确 smoke case/数据行；discovery、选择集合和实际 TRX 必须一致。必需项缺失、重复、跳过、失败或中止均不算通过。

## 必须保留的覆盖

61 项功能与配置保留清单建立当前 owner、成功/失败/边界用例和同源码证据。包括 C/C++、三种 import、两种 Runtime 模式、aggregate 与 callback、编码、allocator、生命周期、扩展、缓存、路径安全、worker 和发布恢复。Engine 的独立 provider 测试不引用默认平台实现。

新旧测试数量相同不能证明行为等价。移除私有反射测试后，由公开生成、语义、清单与真实消费者行为覆盖；历史实现内部的分配/节点身份微测试不再冒充公开契约。

## 性能

10,000 声明预算保持：Windows/Linux 冷生成 60 秒、热恢复 10 秒；macOS Intel 为 120/30 秒。单 worker 2 GiB、默认会话总峰值 4 GiB。删除输出后核对完整文件集合与每个字节。最终受控对比采样五次，记录中位数、波动与超过 10% 的可重复回退；不能放宽预算或关闭输入检查换绿。

## 证据

实现存在、本地编译、本地测试、候选包消费、远端 CI 和公共发布分别记录。dirty 工作区或历史 3.x 回执不能认证 4.0 发布。当前问题与审查写入 records/acceptance，执行证据放 artifacts；这些文档本身不声明门禁通过。
