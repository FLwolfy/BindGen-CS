# CI failure remediation (2026-09-24)

This note records the fixes for the all-red desktop matrix after the BindGen-CS refactor. It is an implementation report, not a substitute for new same-revision runner reports.

## Supplied complete runner logs: second failure set

The six jobs for commit `f70cbb4` reached build/test execution. The attached logs isolate these failures; one test failure should not be mistaken for the others on the same host.

| Runner | Observed failure | Change in this checkout |
| --- | --- | --- |
| Linux x64 build and acceptance | `g++: unrecognized command-line option '--target=x86_64-unknown-linux-gnu'` | Direct, CMake, and Meson providers now add Clang's target flag only for a Clang-style driver; GNU drivers use their configured toolchain target. |
| Windows x64 build | MSBuild linked `provider_msbuild.dll` without `/DLL`, then failed with `LNK1561` | Generated VC project explicitly sets `LinkDLL` and `/DLL`; the plan test checks both. |
| Windows x64 build | Native ABI callback test expected an `nint` public overload that the generator did not promise | The test supplies a call-only callback contract and invokes the generated typed delegate overload across the actual DLL boundary. |
| Windows x64 acceptance | Project-level `dotnet test --no-build` looked for `bin/x64/Release` while the solution built `bin/Release` | The Bash acceptance driver sets `Platform=AnyCPU` for managed project commands; native MSBuild steps still pass their target platform explicitly. |
| macOS Intel build and acceptance | Homebrew LLVM 20 libclang parsed Xcode libc++ ahead of the compiler-discovered libc++, causing `cwchar` conflicts; later tests saw missing bridge output | Host parser now prefers the selected compiler's standard-library include roots, using the SDK libc++ only as a fallback. LLVM 20 discovery and the affected tests pass on the available macOS arm64 host; an Intel rerun is still required. |

Independent hardening from this audit: default C safety suppression now reaches Binding IR and the emitter, `native-build --output` is relative to the invocation directory, published parser packages declare the complete available desktop RID dependency closure, and native assets are rejected when PE/ELF/Mach-O CPU identity disagrees with their target. The source-only README quick start and shim walkthrough were run locally. A complete local macOS arm64 acceptance run now passes; a fresh hosted x64 matrix is still required before those targets can be called accepted.

The local functional matrix also exposed a correctness/performance-gate mix-up: the real-library script failed when its wall-clock check reported 435 seconds for cimguizmo IR generation, although an immediate repeat completed in 2.45 seconds. Functional CI now reports elapsed time and checks generated output, while a controlled performance run can opt into hard time budgets with `BGCS_ENFORCE_GENERATION_BUDGETS=1`. Default safety suppression was also extended to instance and pointer-handle member surfaces, with a regression test.

The five pinned real-C-library fixtures opt into `StrictSafetySeverity=Warning` because their existing cross-target source/public-API snapshots intentionally cover broad raw and inferred APIs without project ownership contracts. This is test-fixture configuration, not the shipped default; a separate default-policy test checks that uncertain friendly APIs disappear while raw ABI remains.

The one-step `bridge` command now exposes `CSharpStrictSafetySeverity`, passed to its C# rebind configuration. The bimg fixture explicitly selects `Warning` to retain its reviewed cross-target API snapshot; ordinary projects still default to `SuppressFriendly`.

When CMake or Meson is allowed to discover its own compiler, BGCS no longer assumes the result is Clang and does not inject a Clang-only target flag. An explicit Clang driver retains `--target` behavior; an explicit GNU driver relies on its configured cross-toolchain. Binary staging still checks the resulting target format and CPU identity.

| Failure | Root cause | Correction |
| --- | --- | --- |
| macOS Intel restore `NU1102` | ClangSharp 20.1.2 publishes no `osx-x64` native runtime packages | Remove the nonexistent references; build the matching companion on Intel with LLVM 20, stage a relocatable dependency closure, and package it as a native RID asset |
| Linux `std::expected` bridge compile | The selected Clang driver used a C++ standard library without that C++23 header | Select and probe the GCC C++ driver in Linux CI; keep the bridge feature and native invocation test |
| Windows acceptance `MSB1008` | MSYS rewrote `/m:1` and `/nodeReuse:false` as paths | Use dash-prefixed MSBuild switches in Bash scripts |
| Windows x86 target macros | Parser options retained host x64 macros across retargeting | Replace generated target macros, sysroot arguments, and discovered target include paths on reconfiguration |
| Windows packed native ABI | Sequential C# layout omitted the analyzed native pack/size | Emit analyzed `Size` and `Pack` for structs as well as unions/opaque storage |
| Windows native-build auto provider | An explicit GNU-style `clang++.exe` was treated as `clang-cl.exe` | Classify compiler command-line personality; reject unknown automatic drivers |
| Windows MSBuild v100 toolset | An older MSBuild could win PATH discovery | Prefer the current Visual Studio MSBuild and set the generated project/toolset explicitly |
| Windows custom shim export | A separate shim translation unit saw `dllimport` instead of `dllexport` | Put the bridge build define in the manifest for every source and make `API_INTERNAL` an implementation-side export |

The Intel runtime bootstrap is in `scripts/setup-macos-x64-clang-runtime.sh`. It checks C++23 `std::expected`, builds the native companion from the exact managed ClangSharp tag, stages dependent dylibs with loader-relative IDs, and includes license notices. The release workflow transfers this asset from its accepted Intel job into the final parser package, asserts the required native files are present, and the clean Intel package consumer clears `BGCS_CLANG_RUNTIME_DIR` before parsing a header. The repository's `global.json` keeps the SDK within .NET 9 across images.

## Local evidence

- .NET solution build: 0 warnings, 0 errors on macOS Arm64.
- Complete local macOS Arm64 acceptance matrix: passed; all ten mandatory categories are 9.0/10.0 in `artifacts/acceptance/report.md` (uncommitted working tree).
- `dotnet restore src/BGCS.CppAst/BGCS.CppAst.csproj -r osx-x64`: passed, eliminating the missing-package restore failure.
- `BGCS.CppAst.Tests`: 111 passed; target-retargeting regression passed after the parser-options change.
- `BGCS.Cpp2C.Tests`: 70 passed, including provider and optional C# safety-policy regressions.
- `BGCS.Generation.Tests`: 87 passed, including native packed ABI runtime and suppressed-member verification.
- `BGCS.Tool.Tests`: 24 passed, including native-build and C++ schema regressions.
- Native runtime override was exercised on macOS Arm64 with the bundled ClangSharp 20 libraries.
- Public API compatibility gate: seven deliberately updated pre-release assembly baselines match.
- Bash syntax, YAML parsing, and `git diff --check`: passed.

## Evidence still required

No code was pushed from this task. A new GitHub Actions run on the same revision is required to verify Windows x64 `clang-cl`/MSBuild/DLL calls, Linux x64 C++23 bridge and package consumer, macOS Intel runtime bootstrap and clean consumer, and the complete acceptance and release-candidate reports. A configured workflow and cross-RID restore are not host acceptance.
