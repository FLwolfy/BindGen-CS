# BGCS NuGet Publishing

## Release model

All BindGen-CS packages use one version and are published as one validated release set:

- `BGCS`
- `BGCS.Cpp2C`
- `BGCS.Runtime`
- `BGCS.Intermediate`
- `BindGen-CS` (the `bindgen-cs` .NET tool)
- `BGCS.CppAst` (transitive implementation package)
- `BGCS.Core` (transitive implementation package)
- `BGCS.Language` (transitive implementation package)

Consumers normally install only `BGCS`, `BGCS.Cpp2C`, or `BGCS.Runtime`. Command-line users install `BindGen-CS` as a .NET tool. NuGet restores implementation packages transitively.

## Local package validation

```bash
./scripts/test-nuget-packages.sh
```

This command packs the complete dependency closure, restores the three public packages into a clean consumer project, compiles it, and runs it. The full test matrix invokes the same package validation.

## Automated publishing

The release workflow is `.github/workflows/publish-bgcs-runtime-nuget.yml`.

It runs restore, build, all tests, package-closure validation, generates `artifacts/supply-chain/sbom.spdx.json` and `provenance.slsa.json`, uploads that evidence, and only then pushes packages and symbol packages. Every package and symbol package is covered by SHA-256. Publishing is triggered by either:

- a unified `v*` tag, such as `v1.2.3`; or
- the manual workflow with a release version.

A normal branch commit or push does **not** publish packages. It may run ordinary CI, but this release workflow starts only for a matching tag or a manual dispatch.

## What GitHub OIDC does

`actions/attest` asks the GitHub-hosted release job for a short-lived OIDC identity bound to the repository, workflow, commit, and run. GitHub uses that identity to produce verifiable provenance and SBOM attestations for the package files. No long-lived Sigstore signing key is stored in the repository.

OIDC signs evidence; it does not authorize NuGet upload. The final `dotnet nuget push` step separately requires `NUGET_API_KEY`.

The release publishes automatically only when all of these conditions are true:

1. GitHub Actions is enabled and the workflow is present on the tagged commit.
2. A `v*` tag is pushed, or an authorized user manually dispatches the workflow.
3. Linux x64, Windows x64, and macOS x64 release-candidate jobs all pass.
4. Build, tests, public API, license/vulnerability, package-closure, and supply-chain steps pass.
5. The repository permits `id-token: write` and artifact attestations for this workflow.
6. `NUGET_API_KEY` exists and can publish every package ID in the release set.
7. Any branch/tag protection, environment approval, or organization policy has been satisfied.

If any prerequisite or gate fails, the package push step is not reached. A successful ordinary CI run alone is therefore not a release.

## Required secret

Set the GitHub Actions secret `NUGET_API_KEY`. Its NuGet package scope must allow all eight package IDs listed above.

## Release

```bash
git tag v1.2.3
git push origin v1.2.3
```
