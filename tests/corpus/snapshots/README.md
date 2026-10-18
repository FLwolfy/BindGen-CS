# Reviewed generated corpus snapshots

The six `*.windows-x64.json` files are the first generator-only Windows x64
source/API baselines for the locked corpus. Review covered the configured public
operations, raw pointer and scalar signatures, generated native helper/bridge
bodies, DTO storage assertions, exception boundaries and exact exclusion policy.
The complete managed source was compiled with warnings as errors; SDL and bimg's
generated native translation units were syntax-checked with the explicit MSVC
toolchain and Windows SDK. The generator's final source-contract checks remain
enabled. Hashes identify the reviewed content; they do not replace those checks.

Pointer lifetimes and ownership are not inferred from these libraries' familiar
API names. For example, C string results and ImGui buffer pointers retain their
raw address surfaces. SDL's portable overflow operations remain present; only
the two compiler-conditional implementation helpers recorded in
`../upstream-limitations.json` are excluded. bimg retains its exact, independently
checked DTO field types and offsets and native status/error boundaries.

These baselines were reviewed from a local generator-only implementation
checkpoint. They certify neither upstream linking/invocation nor Linux, macOS,
other ABIs, installed products or a release revision. The separate upstream
consumer gate must still execute the libraries. Other platforms need their own
source/API review; a Windows hash is not an automatic baseline for them.

Snapshot schema 2 and its sole quoted corpus-include-root normalization are
described in `../README.md`. No generated file is rewritten to match a baseline.
