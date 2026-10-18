# 测试与证据

[English](testing.md) | [验收](acceptance.cn.md) | [包](packages.cn.md)

必需 CI 共四个任务：Windows 完整生成器测试、Linux 生成器 smoke、macOS 生成器 smoke，
以及一个完整产品安装检查。核心功能/配置基线仍为强制验收。AOT/Wasm 应用构建只作为
手动私有检查，不再增加必需 CI 任务。调整的是执行安排，不是目标事实、源码生成契约
或既有性能限制。

## 生成器与核心功能

```bash
dotnet build BindGen-CS.slnx -c Release
dotnet test BindGen-CS.slnx -c Release --no-build
python scripts/generate-config-docs.py --configuration Release --check
```

使用固定的 .NET 10 SDK/runtime 及匹配 worker/parser 资源闭包。原生调用 fixture 还需
显式选定编译器、SDK 头文件和链接器；这些属于仓库测试，不属于生成产品。
语料使用锁定上游 revision 和独立审阅的源码/API 快照；编译、链接、调用不能修补生成
源码或静默删掉公共声明。快照 candidate 不能自动成为 baseline。

完整测试覆盖原生事实与闭包、ABI/类型/布局拒绝、类型化 C++ 协议、三种 import、Runtime
交付、ownership/callback/async 清理、源码处理器、插件、缓存失效、worker 取消/崩溃、
发布恢复和路径安全。精确发现与执行回执防止遗漏、重复、跳过或失败被写成完整通过。

架构、API、文档和供应链审计使用不随产品交付的 `eng/BGCS.RepositoryTools`，不会给产品
增加构建/审计命令。其他测试或消费者使用共享程序集时，不得覆盖那些构建输出。

## 保留原性能预算

`eng/acceptance/performance-budgets.json` 对 10,000 声明的限制保持不变：

| 宿主 | Cold | Warm |
| --- | ---: | ---: |
| Windows x64 | 60 秒 | 10 秒 |
| Linux x64 | 60 秒 | 10 秒 |
| Intel macOS | 120 秒 | 30 秒 |

worker/session 内存上限仍为 2 GiB / 4 GiB。每个宿主保留预算，smoke 安排不豁免。
`scripts/test-performance-budget.sh` 检查数量、完整产物 owner 和删除输出后的逐字节恢复；
仅 emission 命中不能证明完整流水线复用。更严格的显式覆盖需记录。

真实库生成预算仍为 cimguizmo 15 秒、cimgui 30 秒、SDL3 45 秒、miniaudio 60 秒、
bgfx 30 秒、bimg 15 秒。功能运行记录耗时，受控宿主的专门
`BGCS_ENFORCE_GENERATION_BUDGETS=1` 运行执行这些限制；不能提高超时或省略字节检查来通过。

## 产品安装

必需安装任务打包实际四产品，在私有干净工作区消费，检查 SDK/CLI 生成、普通/高级插件、
worker 准备、Runtime package/source 使用及声明的 archive/resource 闭包。必须使用回执中
准确的包字节，不能用仓库工程引用或全局旧包替代。见[包](packages.cn.md)。

## 可选手动 AOT/Wasm 应用检查

```bash
python scripts/test-native-aot-bindings.py
python scripts/test-wasm-bindings.py
python scripts/test-wasm-bindings.py --aot
```

这些仓库 harness 编译/链接生成代码并运行实际应用，可能需要下载 workload、编译器、
浏览器和大量磁盘空间，只在明确要求时作为私有检查执行。生成器不执行这些工作；目标/
runtime 能力检查和静态 adapter 源码生成仍为正常产品功能。

选定私有 fixture 后仍须保留全部断言、独立原生 oracle 和故障注入。缺少前提或调用失败
表示失败/不完整，不能记成成功。回执标明 revision、宿主、runtime、工具链与范围，
不据此宣称所有平台认证。

## 记录方式

源码实现、本地 dirty-tree 证据、干净 revision 核心验收及发布证据分开记录。
保留失败日志和准确输入。旧回执不能认证新源码；配置了 workflow 不表示它已经运行。
