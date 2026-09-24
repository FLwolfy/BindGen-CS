# BGCS NuGet 发布

[English](publish.md) | [Wiki](README.cn.md)

## 发布集合

所有 BindGen-CS 包使用同一个版本并作为一个经过统一验证的 release set 发布：

- `BGCS`
- `BGCS.Cpp2C`
- `BGCS.Runtime`
- `BGCS.Intermediate`
- `BindGen-CS`
- `BGCS.CppAst`
- `BGCS.Core`
- `BGCS.Language`

普通使用者通常只安装 `BGCS`、`BGCS.Cpp2C`、`BGCS.Runtime` 或 .NET tool `BindGen-CS`；实现包由 NuGet 传递恢复。

## 本地验证

```bash
./scripts/test-nuget-packages.sh
```

该脚本 pack 完整依赖闭包，在 clean consumer 中恢复三个公开包、编译并运行。完整验收矩阵也会执行同一 gate。

macOS Intel 先运行 `bash scripts/setup-macos-x64-clang-runtime.sh`，再把 `BGCS_CLANG_RUNTIME_DIR` 设置为脚本输出目录。打包会纳入可重定位的 Clang 20 dylib 与许可证文件；clean consumer 会清除该覆盖设置，实际从 NuGet 包加载 native 资产。发布工作流从已验收的 Intel job 下载 runtime，并强制检查最终 `BGCS.CppAst` 包中包含两个必需 dylib。

## 什么会触发发布

发布 workflow 是 `.github/workflows/publish-bgcs-runtime-nuget.yml`，只由以下事件触发：

- push `v*` tag，例如 `v1.2.3`；
- 在 GitHub Actions 中手动运行，并输入 release version。

**普通 branch commit/push 不会发布。** 普通 CI 全绿也不等于 release；必须显式 push release tag 或手动触发。

Workflow 会先运行 Linux x64、Windows x64、macOS x64 release-candidate 验收，然后执行 restore、build、全部 tests、public API gate、license/vulnerability gate、clean package consumer、SBOM/provenance 生成和 OIDC attestation，最后才 push NuGet packages。

## OIDC/Sigstore 的作用

`actions/attest` 从 GitHub-hosted release job 获取一个绑定 repository、workflow、commit 和 run 的短期 OIDC identity，并用它为 package provenance 与 SBOM association 生成可验证 attestation。仓库不保存长期 Sigstore 私钥。

OIDC 只负责签署供应链证据，不负责 NuGet 上传权限。最终 `dotnet nuget push` 仍需要 GitHub Actions secret `NUGET_API_KEY`。

自动发布成功必须同时满足：

1. GitHub Actions 已启用，tag 所指 commit 包含 release workflow；
2. push `v*` tag，或由有权限的用户手动触发；
3. Linux x64、Windows x64、macOS x64 release-candidate 全部通过；
4. build/test/API/license/vulnerability/package/supply-chain gate 全部通过；
5. 仓库允许该 workflow 使用 `id-token: write` 和 artifact attestation；
6. `NUGET_API_KEY` 已设置，并有权发布全部八个 package ID；
7. 仓库/组织配置的 tag protection、environment approval 等策略已经满足。

任一条件失败都会阻止执行最终 push。因此，“提交代码后 CI 正确”还不够；“发布 tag + 全部 release gate 通过 + OIDC 权限 + NuGet secret 正确”才会自动发布。

## 发布命令

```bash
git tag v1.2.3
git push origin v1.2.3
```

该 tag push 会启动 release workflow。不要在完整 desktop release-candidate 证据未通过时创建正式 tag。
