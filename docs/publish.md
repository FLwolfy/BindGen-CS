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

It runs restore, build, all tests, package-closure validation, and only then pushes packages and symbol packages. Publishing is triggered by either:

- a unified `v*` tag, such as `v1.2.3`; or
- the manual workflow with a release version.

## Required secret

Set the GitHub Actions secret `NUGET_API_KEY`. Its NuGet package scope must allow all eight package IDs listed above.

## Release

```bash
git tag v1.2.3
git push origin v1.2.3
```
