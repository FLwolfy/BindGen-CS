# BGCS NuGet 发布

[English](publish.md) | [包与分发](packages.cn.md) | [发布历史](releases/README.md)

## 发布内容与版本

用户产品只有 `BGCS`、`BGCS.Contracts`、`BGCS.Runtime`、`BindGen-CS`。
Cpp2C、Frontend、Infrastructure、CSharp 等实现程序集随完整 SDK/tool 交付，
不作为用户必须拼装的独立功能产品。

实体集合以 [`product-packages.json`](../distribution/manifests/product-packages.json) 为准：

- 四个产品的 `.nupkg`；
- 清单中 symbol products（BGCS、Contracts、Runtime）的 `.snupkg`；
- 六个声明桌面宿主 RID 的 `BindGen-CS.<rid>` 包，包含 Intel macOS；
- 携带已接受 Intel parser 闭包的技术包 `BGCS.ParserRuntime.osx-x64`。

打包及安装消费门禁拒绝缺失、多余和变化的 archive，包大小上限也由清单给出。
用户安装工具 pointer 包，RID 与解析器依赖属于分发细节。

统一版本来自 `Directory.Build.props`，同时必须有已审阅的
`docs/releases/<version>.md`。生成器、Runtime 和默认消费程序集均为 .NET10。
`global.json` 允许同代 SDK 的开发环境；发布 CI 严格使用
`eng/toolchains/toolchains.lock.json` 锁定的 SDK/runtime/workload。

## 本地包验证

```bash
bash scripts/test-nuget-packages.sh
```

脚本在可清理的独立消费工作区中打包并安装产品，使用私有 NuGet 缓存，避免旧安装字节
满足新的 restore。`BGCS_PACKAGE_TEST_ARTIFACTS_ROOT` 可选择独立产物根目录。

完整打包需要已接受的 Intel parser supplement。Intel macOS 上运行
`bash scripts/setup-macos-x64-clang-runtime.sh`，得到锁定来源、重定位库、摘要与许可证。
`BGCS_CLANG_RUNTIME_DIR` 用于执行，`BGCS_OSX_X64_PACKAGE_RUNTIME_DIR` 用于打包；
后者进入技术包 `BGCS.ParserRuntime.osx-x64`，不是旧的独立 Frontend 产品。
CI 下载并验证同 revision 接受的资源。host-only 本地检查明确属于局部验证，不能代替
完整产品认证。

SDK、Contracts、Runtime、工具的消费要求见
[包认证](packages.cn.md#打包与认证)。本地报告不代表最终包已经上架，也不认证其他平台。

## 自动发布与准确证据

工作流是 [publish-bgcs-runtime-nuget.yml](../.github/workflows/publish-bgcs-runtime-nuget.yml)。
正常自动链路是：

1. main push 执行 Windows 完整生成器检查和 Linux/macOS 生成器 smoke，保留核心基线
   及原有各宿主性能预算。
2. 一个完整产品安装任务消费实际 SDK/tool/Contracts/Runtime 包。AOT/Wasm 应用构建
   是手动私有检查，不属于必需发布任务。
3. 产品认证成功触发发布 preflight，认证当前 main 候选、准确 workflow/run 身份和
   下载的 artifact 字节。
4. 按 `eng/acceptance/required-release-gates.json` 检查作业、功能/配置证据和安装报告，
   不硬编码历史作业数量。缺失、失败、跳过、过期或相互矛盾的必需证据会阻断发布。
5. 重建接受的 revision 并消费实体包，再执行 API、依赖、源码、供应链和包/源码摘要检查。
   原始准确 Host 证据重新核验，不能用没有记录的部分重跑替代。
6. attestation 与再次实体包检查完成后，创建 tag 前重查 main 和最新接受的 runs。
   `v<version>` 必须指向接受的 commit，已有不同 tag 不会移动。后续发布在同一 job 继续，
   因为 `GITHUB_TOKEN` 创建的 tag 不会再次启动 push workflow。
7. OIDC 授权 NuGet 上传，必要技术/library/RID 包先于工具 pointer。公共索引、对应 payload
   与干净四产品安装全部通过后才发布 GitHub release。

version tag push 和手动 dispatch 也只会请求相同 preflight，不会绕过门禁。
手动运行必须选择 main，版本与源码一致；新的 main 候选使旧自动请求失效。
已有发布也需要重验 immutable tag 和一致包内容，不能因 release 已存在就跳过证据。

## 凭据、签名与完成标准

仓库需要允许 Actions、artifact 读取、release 写入、`id-token: write` 和 attestation。
nuget.org 的 trusted publisher 需要匹配 owner `FLwolfy`、repository `BindGen-CS`、
workflow `publish-bgcs-runtime-nuget.yml` 及工作流 environment（当前为空）。
`NUGET_USER` 是拥有全部产品、技术包与 RID 包权限的 nuget.org 用户名。
仓库、组织、tag 和 environment 保护规则也必须满足。

`actions/attest` 为实体包生成 OIDC 绑定的 provenance 与 SBOM association；
`NuGet/login` 换取短期上传 key。仓库不保存长期 NuGet API key 或签名私钥。
实际签名 bundle 和成功上传只能由授权 job 提供，本地 provenance 文本不能替代。

上传后核对公共包内容并干净安装四产品。symbols 的独立 push 必须成功，但不宣称
symbol-server 已完成索引。只有 release workflow、公共消费检查与 GitHub release
全部成功才算发布完成；此前3.0.0安装示例以实际发布为前提。

NuGet 版本不可变。部分上传失败只能用相同已接受内容重试；公共接受后的源码或包内容
变化需要新版本，不能以删除替换已发布版本恢复。

## 历史 GitHub 归档

独立 `Archive historical releases` workflow 根据 `scripts/historical-releases.json`
和[发布历史](releases/README.md)维护旧记录，下载原始 NuGet 包并检查身份、来源 commit、
ZIP 和摘要，实际上传并验证后才发布草稿；它不重新推送历史 NuGet 版本，也不把缺失旧依赖
伪装为当前成功重建。原始 tags 和现有资产保留。

本地可运行 `python scripts/archive-historical-releases.py prepare` 与 `verify`。
真正 publish 需要授权 token；若新历史 tag 指向修改 workflow 的旧 commit，须先由有相应
workflow 授权的维护者创建清单中精确 tag，归档 job 再核验并继续。重试保留匹配的资产，
不覆盖不同来源或摘要的内容。
