# BGCS.Runtime NuGet Publishing

## Workflow

Automatic publishing is configured via GitHub Actions:

- Workflow file: `.github/workflows/publish-bgcs-runtime-nuget.yml`
- Tag trigger: `runtime-v*` (example: `runtime-v1.0.0`)
- Manual trigger: `workflow_dispatch` with `version` input

## Required Secret

Set this repository secret in GitHub Actions:

- `NUGET_API_KEY`

## NuGet API Key Recommendation

When creating the NuGet API key:

- Scope the package to `BGCS.Runtime` (recommended)
- Use push permission for package publish

## Release

```bash
git tag runtime-v1.0.0
git push origin runtime-v1.0.0
```
