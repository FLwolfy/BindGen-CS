# BGCS NuGet Publishing

## Workflow

Automatic publishing is configured via GitHub Actions:

- Workflow file: `.github/workflows/publish-bgcs-runtime-nuget.yml`
- Tag triggers:
  - `bgcs-v*` -> `BGCS`
  - `cpp2c-v*` -> `BGCS.Cpp2C`
  - `runtime-v*` -> `BGCS.Runtime`
- Manual trigger: `workflow_dispatch` with `package` and `version` inputs

## Required Secret

Set this repository secret in GitHub Actions:

- `NUGET_API_KEY`

## NuGet API Key Recommendation

When creating the NuGet API key:

- Scope packages to `BGCS`, `BGCS.Cpp2C`, and `BGCS.Runtime` (recommended)
- Use push permission for package publish

## Release

```bash
git tag bgcs-v1.0.0
git push origin bgcs-v1.0.0

git tag cpp2c-v1.0.0
git push origin cpp2c-v1.0.0

git tag runtime-v1.0.0
git push origin runtime-v1.0.0
```
