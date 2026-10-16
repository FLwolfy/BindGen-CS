# BGCS 3.0 实施与认证路线图

[English](roadmap.md) | [架构](architecture.cn.md) | [验收](acceptance.cn.md)

BGCS 3.0 将源代码事实、原生 ABI 与语义、C++ 桥接操作、托管投影、C# 生成、原生构建、Runtime 和集成分开。目标是用户入口精简、内部模块可独立替换。不保留旧生成器 API、平铺配置读取器、迁移工具或兼容包装；基线为 .NET 10/C# 14、C17、C++23。

源码实现、逐项审核和发布认证是不同状态。[实施状态](implementation-status.cn.md)记录本地开发证据，[发布证据规则](release-evidence-state-flow.md)说明如何为干净提交取得平台、安装包及发行证明。编译成功或目录拆分完成，都不能单独关闭路线图。

| 阶段 | 实施内容与必要交付证据 |
| --- | --- |
| 0：基线 | 十三个生产工程、精确依赖规则、当前功能与配置清单、确定性的 net10 构建及锁定工具链。 |
| 1：Contracts | 脱离解析器的不可变 native、bridge、interop、managed 模型，版本化序列化和 Contracts-only 插件；无原生句柄或外层依赖。 |
| 2：目标与 worker | 分开宿主解析器、目标 ABI、SDK、托管环境；验证资源；覆盖取消、超时、异常协议及真实原生崩溃隔离。 |
| 3：Analysis | 声明闭包、独立存储与按值 ABI 检查、带证据的 ownership/text/buffer/callback/async/error 语义；区分 raw 可表达性与 friendly 资格。 |
| 4：C++ bridge | 类型化操作、标准及配置式 family lowering、模板、继承转换、异常边界、虚代理与 shim；重新解析 C 导出并合并语义。 |
| 5：C# backend | 三种 imports 共用投影与计划；实例/静态、raw/friendly、确定命名、单/多文件、源码处理、元数据及最终契约复核。 |
| 6：Runtime | 模块、资源与调用 lease，owned/borrowed 对象，回调及异步关闭，严格 codec、匹配 allocator，包/源码一致性及受限特殊存储。 |
| 7：Host | CLI/SDK/workspace/MSBuild 共用流水线；插件启动与冻结；完整缓存输入及产物集合；候选验证与可恢复发布。 |
| 8：集成与分发 | 原生 provider、二进制/导出/资产核对、显式启用的独立 MSBuild TaskHost；四个产品及完整解析器执行闭包、干净安装消费者。 |
| 9：首发 | 同提交 Windows x64、Linux GNU x64、macOS Intel、浏览器解释/AOT 及 NativeAOT；锁定真实库、故障注入、性能、API、许可证和供应链。 |
| 10：后续认证 | ARM64、musl、Android、iOS 设备/模拟器、FreeBSD 分别取得解析、生成、编译、链接、调用和安装证据。 |

公开产品为 `BGCS`、`BGCS.Contracts`、`BGCS.Runtime`、`BindGen-CS`。内部程序集、解析器资源和宿主工具分包属于分发细节，用户无需为每个源码工程安装一个包。

平台适配表达真实 CPU、ABI、SDK、模块加载和运行时限制。存在目标描述不代表调用已认证。未支持的按值布局或缺少生命周期证明时，应拒绝或只保留正确 raw 路径；相同 `sizeof` 或用户声明不能变成 Verified ABI 证据。浏览器中需要阻塞关闭的虚回调属于明确能力限制，不能凭空生成安全包装。

发布需要全部冻结功能及配置身份具有审核后的实现和精确可执行断言。CI 为一个干净 revision 证明这些声明。可信发行链随后核对所有强制 job 和真实安装归档，创建不可移动的版本标签，推送 NuGet，核对公共包字节并执行干净公共源安装。仓库内 `releaseReady` 保持 false；只有独立成功预检才能推导可发布状态。

这些门禁通过后才进入维护状态。普通 lowering 不修改 frontend，普通目标不横跨 emitter 修改，Runtime 消费者不携带生成器；工具链和 ABI 变化重新执行受影响证据。这是可检查的维护边界，不能承诺未来 C++ 和新运行平台永远无需工程工作。

[归档的 2.x 路线图](roadmap-2.x.cn.md)仅保留历史，不是 3.0 的使用及完成契约。
