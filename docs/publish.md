# BGCS NuGet Publishing

## Release model

All BindGen-CS packages use one version and are published as one validated release set:

- `BGCS`
- `BGCS.Cpp2C`
- `BGCS.Runtime`
- `BGCS.Intermediate`
- `BindGen-CS` (the `bindgen-cs` .NET tool pointer package)
- `BindGen-CS.win-x64`, `BindGen-CS.win-arm64`, `BindGen-CS.linux-x64`, `BindGen-CS.linux-arm64`, `BindGen-CS.osx-arm64` (RID-specific tool packages)
- `BGCS.CppAst` (transitive implementation package)
- `BGCS.Core` (transitive implementation package)
- `BGCS.Language` (transitive implementation package)

Consumers normally install only `BGCS`, `BGCS.Cpp2C`, or `BGCS.Runtime`. Command-line users install `BindGen-CS` as a .NET tool. NuGet restores implementation packages transitively.

### Why the tool is packed per runtime identifier

The parser depends on a libclang and libClangSharp runtime for every published desktop RID. A single self-contained tool package therefore carries around 300 MB of native assets and is rejected by nuget.org, which refuses any package above 250 MB. `ToolPackageRuntimeIdentifiers` in `src/BGCS.Tool/BGCS.Tool.csproj` splits that into a small `BindGen-CS` pointer package plus one package per RID, each well under the limit. `dotnet tool install --global BindGen-CS` resolves the pointer package and downloads only the current platform's package.

This packaging format is produced and consumed by the .NET 10 SDK, so `global.json` pins SDK `10.0.100`. The assemblies still target `net9.0`; the .NET 9 runtime is what executes them, and CI installs both.

## Local package validation

```bash
./scripts/test-nuget-packages.sh
```

This command packs the complete dependency closure, restores the three public packages into a clean consumer project, compiles it, and runs it. The full test matrix invokes the same package validation.
Set `BGCS_PACKAGE_TEST_ARTIFACTS_ROOT` to a dedicated absolute directory to keep test outputs separate from the repository's `artifacts/` directory.

On macOS Intel, first run `bash scripts/setup-macos-x64-clang-runtime.sh`. Set `BGCS_CLANG_RUNTIME_DIR` to its output directory for local parser execution, and set `BGCS_OSX_X64_PACKAGE_RUNTIME_DIR` to the same directory when packing. These are separate: the first overrides native loading on the current host; the second contributes relocatable Clang 20 dylibs and license notices to `BGCS.CppAst`. The clean consumer clears the loading override and uses the package's own native assets. The release workflow uploads the accepted Intel runtime from its macOS job and includes it in the final parser package. A release fails if either required Intel dylib is absent from that package.

## Automated publishing

The release workflow is `.github/workflows/publish-bgcs-runtime-nuget.yml`.

It runs restore, build, all tests, package-closure validation, generates `artifacts/supply-chain/sbom.spdx.json` and `provenance.slsa.json`, uploads that evidence, and only then pushes packages and symbol packages. Every package and symbol package is covered by SHA-256. Publishing is triggered by either:

- a unified `v*` tag, such as `v1.2.3`; or
- the manual workflow with a release version.

The package-closure test keeps its `BGCS.NativeAsset.Probe` package outside the release artifact directory. The seven library packages each have a `.nupkg` and `.snupkg`; the tool contributes the pointer `.nupkg` and five RID `.nupkg` files, for twenty release files in total. The workflow validates that exact set, and that no tool package exceeds the nuget.org size limit, before signing or uploading anything. RID packages are pushed before the pointer package, which cannot install until they are available.

A normal branch commit or push does **not** publish packages. It may run ordinary CI, but this release workflow starts only for a matching tag or a manual dispatch.

## What GitHub OIDC does

`actions/attest` asks the GitHub-hosted release job for a short-lived OIDC identity bound to the repository, workflow, commit, and run. GitHub uses that identity to produce verifiable provenance and SBOM attestations for the package files. No long-lived Sigstore signing key is stored in the repository.

OIDC also authorizes the upload. `NuGet/login` exchanges the same job identity for a nuget.org API key that expires in one hour (nuget.org trusted publishing), so no long-lived push key is stored in the repository.

The release publishes automatically only when all of these conditions are true:

1. GitHub Actions is enabled and the workflow is present on the tagged commit.
2. A `v*` tag is pushed, or an authorized user manually dispatches the workflow.
3. Linux x64, Windows x64, and macOS x64 release-candidate jobs all pass.
4. Build, tests, public API, license/vulnerability, package-closure, and supply-chain steps pass.
5. The repository permits `id-token: write` and artifact attestations for this workflow.
6. A nuget.org trusted publishing policy matches this repository and workflow file, and `NUGET_USER` is set.
7. Any branch/tag protection, environment approval, or organization policy has been satisfied.

If any prerequisite or gate fails, the package push step is not reached. A successful ordinary CI run alone is therefore not a release.

## Publishing credentials

Publishing uses [nuget.org trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) instead of a stored API key.

1. On nuget.org, under your username, add a trusted publishing policy with repository owner `FLwolfy`, repository `BindGen-CS`, workflow file `publish-bgcs-runtime-nuget.yml`, and no environment.
2. Set the GitHub Actions secret `NUGET_USER` to the nuget.org profile name that owns the packages (not an email address).

The policy owner must be able to push all thirteen package IDs listed above, including the `BindGen-CS.<rid>` IDs that are published for the first time.

## Release

```bash
git tag v1.2.3
git push origin v1.2.3
```
