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

- successful main-branch CI for an unpublished version declared in `Directory.Build.props`, with reviewed `docs/releases/<version>.md` notes;
- a unified `v*` tag matching that declared version; or
- the manual workflow with a release version.

The package-closure test keeps its `BGCS.NativeAsset.Probe` package outside the release artifact directory. The seven library packages each have a `.nupkg` and `.snupkg`; the tool contributes the pointer `.nupkg` and five RID `.nupkg` files, for twenty release files in total. The workflow validates that exact set, and that no tool package exceeds the nuget.org size limit, before signing or uploading anything. RID packages are pushed before the pointer package, which cannot install until they are available.

The release preflight checks the exact source revision against all fifteen CI jobs,
including the independent NativeAOT and Wasm interpreter/AOT consumers. It rejects
missing, failed or skipped jobs. Candidate and packaging jobs check out that same
revision rather than the moving branch head. CI completes the full acceptance on
all three hosts before publication. Its successful macOS Intel acceptance seals
the tested parser runtime and license files with a SHA-256 manifest and uploads
them as an immutable run artifact.

On main, an unpublished declared version with release notes requests publication
after CI succeeds. A version already present as a published GitHub release is
skipped; ordinary subsequent commits do not republish it. A completed CI run for
an older main revision cannot start a release after main has moved forward.

After package validation, attestations and the complete NuGet upload succeed, the
workflow creates a draft GitHub release targeting the accepted commit, uploads the
twenty package files, checksums, SBOM, provenance and attestations, then publishes
the draft. Upload failures leave the draft unpublished; rerunning the failed job
resumes its asset upload. Existing release tags are never moved to another commit.

The publisher downloads the parser artifact by the accepted CI run ID and verifies
every checksum before packing. It never selects artifacts from the latest branch
or a different revision. The CI acceptance is reused instead of repeating the same
three-host candidate jobs. Final build, tests, API, supply-chain and package-consumer
checks still run on the actual versioned release package set. Shell entry points
are invoked explicitly with `bash`, independently of checkout execute bits.

## What GitHub OIDC does

`actions/attest` asks the GitHub-hosted release job for a short-lived OIDC identity bound to the repository, workflow, commit, and run. GitHub uses that identity to produce verifiable provenance and SBOM attestations for the package files. No long-lived Sigstore signing key is stored in the repository.

OIDC also authorizes the upload. `NuGet/login` exchanges the same job identity for a nuget.org API key that expires in one hour (nuget.org trusted publishing), so no long-lived push key is stored in the repository.

The release publishes automatically only when all of these conditions are true:

1. GitHub Actions is enabled and the workflow is present on the default branch and release commit.
2. The declared source version has reviewed release notes, and its exact commit has passed all fifteen ordinary CI jobs.
3. Linux x64, Windows x64, and macOS x64 CI acceptance jobs all pass, and the accepted parser runtime artifact passes checksum verification.
4. Build, tests, public API, license/vulnerability, package-closure, and supply-chain steps pass.
5. The repository permits `id-token: write` and artifact attestations for this workflow.
6. A nuget.org trusted publishing policy matches this repository and workflow file, and `NUGET_USER` is set.
7. Any branch/tag protection, environment approval, or organization policy has been satisfied.

If a prerequisite or gate fails, publication does not proceed. A successful CI run
is the prerequisite for a requested release; publication is complete only when
the release workflow succeeds and the package set and GitHub release exist.

NuGet package versions are immutable. After any package has been accepted by
nuget.org, retry upload failures without changing its contents. Changes to released
code require a new version; deleting and replacing published packages is not a
recovery mechanism.

## Historical GitHub archives

The independent `Archive historical releases` workflow maintains the reviewed
entries in `scripts/historical-releases.json` and the [release history](releases/README.md).
It runs after accepted CI when those entries, their release notes, or the archive
workflow change, or while a reviewed archive remains unpublished. This resumes
interrupted archival work after a later CI fix. It also supports a manual run
against accepted main.

It downloads the original NuGet packages, checks their identities, source commits,
ZIP integrity and hashes, then publishes GitHub drafts only after their uploaded
hashes match. It never repushes historical NuGet versions. Missing NuGet releases
are source-only alpha prereleases; their original source tags are preserved.
The current latest release and its existing assets are retained.

The CLI supports `prepare`, `verify` and `publish`:

```bash
python3 scripts/archive-historical-releases.py prepare
python3 scripts/archive-historical-releases.py verify
```

Publication requires the job's GitHub token with `contents: write`; local
preparation and verification do not require credentials. Already uploaded assets
must have identical hashes before a retry can reuse them. Historical packages
record their original dependencies and actual build commits, including missing
exact dependency versions; the archive does not claim a successful modern rebuild.

Drafts are resolved through the authenticated release list and verified by release
ID before publication. Retries resume the same draft and retain matching assets.
If a new archival tag targets a historical commit that changes workflow files,
[GitHub requires workflow authorization](https://docs.github.com/en/rest/releases/releases#create-a-release),
which the built-in Actions token cannot hold. An authorized repository owner must
first create that alpha tag at the exact source commit in the reviewed manifest;
the archive workflow then verifies the tag and completes the release.

## Publishing credentials

Publishing uses [nuget.org trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) instead of a stored API key.

1. On nuget.org, under your username, add a trusted publishing policy with repository owner `FLwolfy`, repository `BindGen-CS`, workflow file `publish-bgcs-runtime-nuget.yml`, and no environment.
2. Set the GitHub Actions secret `NUGET_USER` to the nuget.org profile name that owns the packages (not an email address).

The policy owner must be able to push all thirteen package IDs listed above, including the `BindGen-CS.<rid>` IDs that are published for the first time.

## Release

Set the unified version in `Directory.Build.props`, add reviewed release notes at
`docs/releases/<version>.md`, and push to main. CI completes before the release
workflow starts. Tags and manual dispatch remain available, with the same CI gate:

```bash
git tag v2.1.0
git push origin v2.1.0
```
