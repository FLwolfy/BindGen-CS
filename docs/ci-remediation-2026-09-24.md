# CI failure remediation (2026-09-24)

This note records the fixes for the all-red desktop matrix after the BindGen-CS refactor. It is an implementation report, not a substitute for new same-revision runner reports.

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
- `dotnet restore src/BGCS.CppAst/BGCS.CppAst.csproj -r osx-x64`: passed, eliminating the missing-package restore failure.
- `BGCS.CppAst.Tests`: 105 passed; target-retargeting regression passed after the final parser-options change.
- `BGCS.Cpp2C.Tests`: 65 passed; shim/provider focused regressions rerun after final bridge changes.
- `BGCS.Generation.Tests`: 85 passed, including native packed ABI runtime verification.
- `BGCS.Tool.Tests`: 23 passed; native-build command regressions rerun after provider classification changes.
- Native runtime override was exercised on macOS Arm64 with the bundled ClangSharp 20 libraries.
- Public API compatibility gate: seven reviewed assembly baselines unchanged.
- Bash syntax, YAML parsing, and `git diff --check`: passed.

## Evidence still required

No code was pushed from this task. A new GitHub Actions run on the same revision is required to verify Windows x64 `clang-cl`/MSBuild/DLL calls, Linux x64 C++23 bridge and package consumer, macOS Intel runtime bootstrap and clean consumer, and the complete acceptance and release-candidate reports. A configured workflow and cross-RID restore are not host acceptance.
