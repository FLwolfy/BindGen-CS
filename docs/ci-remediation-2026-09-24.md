# CI failure remediation (2026-09-24)

This note records the fixes for the all-red desktop matrix after the BindGen-CS refactor. It is an implementation report, not a substitute for new same-revision runner reports.

## Supplied complete runner logs: second failure set

The six jobs for commit `f70cbb4` reached build/test execution. The attached logs isolate these failures; one test failure should not be mistaken for the others on the same host.

| Runner | Observed failure | Change in this checkout |
| --- | --- | --- |
| Linux x64 build and acceptance | `g++: unrecognized command-line option '--target=x86_64-unknown-linux-gnu'` | Direct, CMake, and Meson providers now add Clang's target flag only for a Clang-style driver; GNU drivers use their configured toolchain target. |
| Windows x64 build | MSBuild linked `provider_msbuild.dll` without `/DLL`, then failed with `LNK1561` | Generated VC project explicitly sets `LinkDLL` and `/DLL`; the plan test checks both. |
| Windows x64 build | Native ABI callback test expected an `nint` public overload that the generator did not promise | The test supplies a call-only callback contract and invokes the generated typed delegate overload across the actual DLL boundary. |
| Windows x64 acceptance | Project-level `dotnet test --no-build` looked for `bin/x64/Release` while the solution built `bin/Release` | An initial workaround set `Platform=AnyCPU`; the later runner exposed that this is not a valid solution platform. The follow-up below supersedes it. |
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

## Follow-up runner logs: commit `8a7a42d`

The next six-job CI run had two green managed jobs (Linux x64 and macOS Intel) and four red jobs. The failures have different causes:

| Job | Observed failure | Correction in this checkout |
| --- | --- | --- |
| Windows managed | One of 87 generation tests failed: a declared call-only callback typedef had only the raw `void*` method, not the typed delegate overload required by the native invocation test. | The IR-native emitter now permits the typed overload for an opaque callback typedef only when its lifetime and threading contracts are both explicit. It keeps the delegate alive through native return; a Windows-targeted cross-host generation/compilation regression covers this path. |
| Windows acceptance | `MSB4126`: the solution has `Release|Any CPU`, not `Release|AnyCPU`. | Remove the MSVC developer shell's ambient `Platform=x64` for this acceptance driver and let solution and project commands choose their own valid defaults. Native provider tests still select x64 explicitly. |
| Linux x64 acceptance | All managed tests and five real C-library generation/compilation checks passed, then the script stopped because `api-snapshots.linux-x64.sha256` did not exist. | Missing host baselines now produce candidate SHA-256 manifests and upload the generated inputs for human review, while acceptance remains red. The same run continues through the C++ bridge to collect its candidate too. |
| macOS Intel acceptance | The same missing-baseline stop at `api-snapshots.macos-x64.sha256`, after managed tests and five real C libraries passed. | Same candidate-and-review flow; no Arm64 hash is reused as Intel evidence. |

The candidate manifests are not accepted baselines. After the next same-revision runner completes, review each generated source, public API, and C++ bridge output in its `snapshot-inputs-<target>` artifact. Add only the corresponding target-specific manifests to `tests/real-libraries/`, rerun CI, and require a full acceptance report. Windows may reveal a later independent failure after the restore blocker is removed; do not mark it accepted before the entire job passes.

## Run #35: commit `52836a1`

All three desktop x64 build/test jobs passed. Linux x64 and macOS Intel acceptance completed their managed, C/C++, API, NuGet consumer, performance, and dependency-policy layers, then exited with status 3 solely because their reviewed real-library manifests were absent. Their uploaded `snapshot-inputs` artifacts contain 14 generated files each; every file was independently rehashed against its runner-produced candidate. The six C source/public-API/C++ manifests in this checkout come from those artifacts, not from Arm64 or cross-compilation. The principal platform-specific public-API difference is miniaudio's Linux ALSA/JACK versus macOS CoreAudio surface.

Windows x64 reached warning-free generation and compilation of all five real C libraries, then `sha256sum --check` treated a CRLF manifest's trailing carriage return as part of each filename. The verifier now accepts LF or CRLF manifest lines and `.gitattributes` requests LF for committed manifests. The Windows runner uploaded all ten C generated/API files. Their current byte hashes differ from the older reviewed Windows baselines even after newline normalization, so the two Windows C manifests were updated from those actual host files. A shell regression tests CRLF verification, changed-baseline candidate capture, and missing-baseline fail-closed behavior.

The Windows C++ bimg snapshot was not reached in run #35. If its existing manifest is stale, the next runner now uploads a mismatch candidate and continues the remaining acceptance layers while still failing. No Windows C++ hash is inferred from another platform; Windows cannot be marked accepted until its own full report passes. No code was pushed from this remediation checkout.

## Run #36: commit `95352b6`

All three build/test jobs and the Linux x64 and macOS Intel acceptance jobs passed. Windows x64 acceptance compiled the five real C libraries and the bimg generated bridge/consumer without warnings, then found that its old bimg source/C#/public-API hashes were stale. The `snapshot-inputs-windows-x64-msvc` artifact (ID `10804358689`) contains the four bimg files and a candidate manifest. Their bytes match that candidate. After normalizing line endings for review, `Classes.cpp` is identical to the Linux and macOS x64 output; the Windows C# surface differs in two enum underlying types and two unused empty placeholder-record layouts. A Clang cross-target probe independently confirmed that an ordinary positive-valued C++ enum is `int` under the MSVC target but `unsigned int` under Darwin, and that an empty C record also has target-dependent layout. Neither empty placeholder type is used by the generated callable bridge. The target-specific Windows manifest was updated from the actual Windows artifact, without substituting another target's hashes.

The next layer then stopped in `BGCS.ApiSnapshot` while reflecting an assembly, requesting unavailable `System.Runtime, Version=10.0.0.0` from the .NET 9 process. Its dependency fallback had searched the NuGet cache by assembly name and chosen the highest installed version, independent of the target assembly's `.deps.json`. The snapshot tool now resolves only the exact package/runtime asset pinned by that dependency manifest, lets framework assemblies come from the running framework, and rejects a mismatched assembly identity. A focused test places a newer fake package next to the pinned one and proves that it is never chosen. This is a test-tool dependency-resolution fix, not a change to generated bindings or a disabled API gate.

Run #36 remediation checks on macOS Arm64: warning-as-error solution build passed; 25 tool tests passed; the seven-assembly public API compatibility gate passed; the targeted single-file generation test passed; and all four Windows bimg candidate hashes matched the uploaded bytes. Windows acceptance must still run the changed resolver and all downstream gates on its own runner.

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

The Linux x64 and macOS Intel run #36 acceptance reports are complete for that commit. The Windows x64 job must rerun after the run #36 fixes and pass its remaining public API, NuGet native consumer, performance, dependency-policy, and report gates before the current revision has a complete three-target desktop matrix. A configured workflow and cross-RID restore are not substitutes for that host report. No code was pushed from this remediation checkout.
