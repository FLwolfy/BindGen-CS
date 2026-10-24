# BGCS NuGet publishing

[简体中文](publish.cn.md) | [Packages](packages.md) | [Release history](releases/README.md)

## Release contents and version

The four base user products are `BGCS`, `BGCS.Core`, `BGCS.Runtime` and `BindGen-CS`.
Internal implementation assemblies such as NativeBridge, Frontend, Infrastructure and CSharp
are bundled by the full SDK/tool, not separate products a user must assemble.

The physical set is defined by
[`product-packages.json`](../distribution/manifests/product-packages.json):

- `.nupkg` files for the four base products and five optional `BGCS.Extension.*` implementations;
- `.snupkg` files for its declared symbol products (base libraries and optional extensions);
- `BindGen-CS.<rid>` packages for the declared six desktop host RIDs, including Intel macOS;
- the technical `BGCS.ParserRuntime.osx-x64` package with the accepted Intel parser closure.

Packing and installed-product verification reject missing, extra or changed archives.
The package-size bound also comes from that manifest. Users install the pointer tool
package; its RID package and parser dependencies are distribution details.

`Directory.Build.props` owns the unified version and `docs/releases/<version>.md` must
contain reviewed notes. All generator, Runtime and default consumer assemblies target
.NET10. `global.json` permits development within that SDK generation; release CI uses
the applicable exact SDK/runtime lock in `eng/toolchains/toolchains.lock.json`.

## Local package validation

```bash
bash scripts/test-nuget-packages.sh
```

This packs and installs the declared products in disposable clean consumer workspaces.
`BGCS_PACKAGE_TEST_ARTIFACTS_ROOT` can select a dedicated artifact root. Private NuGet
caches prevent older installed BGCS bytes from satisfying a fresh restore.

Full packaging requires the accepted Intel parser supplement. On Intel macOS,
`bash scripts/setup-macos-x64-clang-runtime.sh` creates it with pinned inputs,
relocated libraries, hashes and licenses. `BGCS_CLANG_RUNTIME_DIR` selects that parser
for execution; `BGCS_OSX_X64_PACKAGE_RUNTIME_DIR` supplies it to packaging. The latter
feeds `BGCS.ParserRuntime.osx-x64`, not an old standalone frontend product.
CI downloads and verifies the same-revision accepted artifact. A host-only local run
is explicitly partial and cannot substitute for complete product certification.

See [package certification](packages.md#repository-package-verification) for SDK, Core,
Runtime and installed tool consumer requirements. The targeted Linux Browser regression
is part of generator CI; broader application matrices are separate. A local
report does not establish final package availability or certify another platform.

## Automatic publication and exact evidence

The workflow is
[`.github/workflows/publish-bgcs-runtime-nuget.yml`](../.github/workflows/publish-bgcs-runtime-nuget.yml).
The ordinary automatic chain is:

1. A main push runs Windows full generator checks and Linux/macOS generator smoke,
   preserving core feature baselines and original per-host performance budgets.
2. One full product-install job consumes the actual SDK/tool/Core/Runtime and optional
   extension packages. Linux smoke runs the fixed Browser Mono Wasm/AOT aggregate and
   callback regression; broader application publishing matrices are not repeated.
3. Successful product certification triggers release preflight. It authenticates the
   current main candidate, exact workflow/run identities and downloaded artifact bytes.
4. Required jobs, feature/configuration evidence and installed-product reports are checked
   against `eng/acceptance/required-release-gates.json`. There is no historical fixed job
   count. Missing, failed, skipped, stale or contradictory required evidence blocks release.
5. Download the accepted package archives, installed-product receipt and authenticated
   repository tooling from the certified runs. Recheck their exact hashes and source
   identities, then generate supply-chain evidence from those same archive bytes.
   Original Host and feature evidence remains linked to the accepted CI run.
6. After attestations and another physical-package check, tag creation rechecks main and
   the latest accepted runs. `v<version>` must point to that accepted commit; an existing
   different tag is never moved. Publishing continues in the same job, since a tag made
   by `GITHUB_TOKEN` does not start a second push workflow.
7. OIDC authorizes NuGet upload. Required technical/library/RID packages are pushed before
   the tool pointer. Public indexing, matching package payloads and clean four-product
   installs must pass before the GitHub release is published.

Version-tag pushes and manual dispatch are additional requests for the same preflight,
not ways to bypass it. Manual dispatch selects main and its version must match the
source declaration. A newer main candidate invalidates a stale automatic request.
An already published release is revalidated against the immutable tag and matching
package payloads; its existence is not a shortcut around current evidence.

## Credentials, signing and completion

The repository must permit Actions execution, artifact reads, release writes,
`id-token: write` and attestations. On nuget.org, configure a trusted publisher for
owner `FLwolfy`, repository `BindGen-CS`, workflow `publish-bgcs-runtime-nuget.yml`,
with the environment matching the workflow (currently none). Set `NUGET_USER` to
the nuget.org profile that owns every product/technical/RID package ID. Repository,
organization, tag or environment protection rules must also be satisfied.

`actions/attest` creates OIDC-bound provenance and SBOM associations for the physical
packages. `NuGet/login` obtains a short-lived upload key; no long-lived NuGet API key
or signing private key is stored in the repository. Actual signed bundles and successful
uploads come only from the authorized job, not a locally generated provenance document.

The workflow verifies public package payloads and installs the four products after upload.
Separate symbol pushes must succeed; that does not claim symbol-server indexing.
Publication is complete only after the release workflow, public-consumption checks and
GitHub release succeed. Before that, installation examples for 4.1.1 are conditional on
actual publication.

NuGet versions are immutable. Retry a partial upload with identical accepted contents;
code or package-content changes after public acceptance require a new version. Do not
replace or delete a published version as a recovery mechanism.

## Historical GitHub archives


The independent `Archive historical releases` workflow maintains the reviewed
entries in `scripts/historical-releases.json` and the [release history](releases/README.md).
It is triggered manually against accepted main. Routine generator CI does not
rebuild or republish historical releases.

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

Existing drafts are resolved through the authenticated release list. New drafts
use the ID returned by their creation request, since release-list indexing can lag.
Assets are uploaded and verified by that ID before publication. Retries resume the
same draft and retain matching assets.
If a new archival tag targets a historical commit that changes workflow files,
[GitHub requires workflow authorization](https://docs.github.com/en/rest/releases/releases#create-a-release),
which the built-in Actions token cannot hold. An authorized repository owner must
first create that alpha tag at the exact source commit in the reviewed manifest;
the archive workflow then verifies the tag and completes the release.
