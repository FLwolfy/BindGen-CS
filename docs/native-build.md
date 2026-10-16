# Native construction and consumption

[Documentation](README.md) · [简体中文](native-build.cn.md) · [Public API](api.md) · [MSBuild](msbuild.md)

BGCS separates understanding native declarations, generating a C ABI bridge,
building native code, verifying its artifact and consuming it from .NET. A
successful parse or C# compile does not prove native ABI or invocation safety.
Clang parsing does not require the final native compiler to be Clang.

## Choose the operation

| Operation | Native behavior |
| --- | --- |
| `generate` | Generates bindings and C++ bridge inputs; does not compile upstream native code. |
| `bridge` | Requests bridge artifacts without requiring a managed projection. |
| `build` | Validates candidates and performs declared sources/prebuilt construction before joint publication. |
| `native-build` | Uses a generated locked neutral native manifest for standalone construction. |
| Consumer MSBuild | Imports exact typed native references into the actual application build. |

The SDK equivalents are `GenerateAsync`, `BridgeAsync`, `BuildAsync`,
`PlanNativeBuildAsync` and `NativeBuildAsync`. See [public API](api.md) for
request/result ownership and private-plan versus published-output paths.

## Build declared sources

```json
{
  "schemaVersion": 1,
  "library": { "id": "widget", "nativeName": "widget_bridge" },
  "source": {
    "language": "cpp",
    "languageStandard": "c++23",
    "headers": ["include/widget.hpp"],
    "includeDirectories": ["include"]
  },
  "targets": [{ "id": "host" }],
  "api": { "namespace": "Example.Widget", "name": "WidgetApi" },
  "nativeBuild": {
    "mode": "sources",
    "provider": "auto",
    "sources": ["src/widget.cpp"]
  }
}
```

`build` includes generated bridge sources and declared upstream sources.
Input files resolve from the configuration that declares them. Declare actual
definitions, link dependencies and SDK inputs; parsing a header does not supply
an external library's implementation. BGCS does not download compilers or SDKs.

`nativeBuild` supports `outputKind`, `buildToolPath`, `auxiliaryToolPath`,
`compilerArguments`, `linkerArguments`, `libraries`,
`libraryDirectories` and optional `packaging`. Defaults and constraints are
in the [generated schema reference](generated/config-reference.md).
Compiler/sysroot/header choices belong to `targets[].settings`, separate
from build-system executable choices.

Parser-specific worker contributors remain isolated during both `build` and
standalone `native-build`. The generated manifest retains their exact parser
identity, executable closure, settings and input origins. Each effective-source
verification starts a fresh worker preflight and recaptures its declared state;
it does not reuse an earlier parse capture or load the contributor in the Host.
Missing activations, changed files or incompatible settings fail before joint
publication. Contributor facts still have to pass the actual native compiler's
independent source and ABI checks.

## Providers and their prerequisites

| Provider ID | Required installed tools |
| --- | --- |
| `auto` | An unambiguous target-compatible compiler/provider. |
| `clang` | Matching Clang driver; LLVM archive tools when requested. |
| `gnu` | Matching GCC/G++ driver and archive tools. |
| `msvc` | MSVC, matching Visual C++ tools and Windows SDK. |
| `clang-cl` | clang-cl plus matching Visual C++ tools/linker and Windows SDK. |
| `cmake` | CMake 3.24 or newer, Ninja and a resolved native compiler. |
| `meson` | Meson 1.0 or newer, Ninja and a resolved compiler; CI locks the actual Meson gate to 1.8.3. |
| `msbuild` | Native MSBuild plus matching Visual C++ tools and Windows SDK. |
| Custom stable ID | A registered `INativeBuildProvider` with complete declared inputs. |

CMake, Meson and native MSBuild providers create their own frozen build graphs
for declared inputs. They do not accept arbitrary existing CMake/Meson/vcxproj
projects as an implicit external build contract. `BGCS.MSBuild` starts BGCS
from a managed project; native provider `msbuild` compiles native code. These
are independent integrations.

Explicit tool paths are authoritative. A missing requested tool or incompatible
driver fails; another PATH compiler cannot silently substitute a different SDK
or ABI. Plans use argument arrays, exact per-command environments and unified
process execution rather than shell-built command strings. User arguments and
response/config inputs are checked for changes to declared target identity.

For direct compiler and native MSBuild providers, `auxiliaryToolPath` is an
explicit archive-tool override. For CMake/Meson it selects Ninja. A direct
compiler provider rejects `buildToolPath`; pin its compiler in target settings.
Clang enforces an explicit linker path through its driver or direct Windows
link execution. GNU rejects an arbitrary explicit linker pin until its driver
mapping can be proven. An implicit driver-selected linker is not independently
pinned merely because the compiler path is known. Explicit export-inspector
paths are also authoritative, including Windows `dumpbin`; a missing override
does not fall back to another installed tool.

Provider presence means an implementation exists. Certification requires its
actual compilation/export/invocation receipts on the stated target and tool
versions, not just a command-plan snapshot.

GNU selection checks the driver's actual bounded `--version` output and file
identity. A `gcc` or `g++` filename alone is insufficient: macOS commonly ships
Apple Clang under those aliases. Request `clang` for Apple Clang, or pin a
genuine GCC/G++ path in target settings. `BGCS_GNU_CC` and `BGCS_GNU_CXX` can
select explicit GNU discovery executables; a missing or wrong-family override
fails rather than falling back. GNU source-view certification runs on Linux,
and on other supported Unix hosts only when genuine GNU tools are explicitly
selected. Clang and GNU receipts remain separate.

`doctor --json` verifies the complete locked host worker closure and reports
`assetDigestsVerified` separately from `parseExecuted = false`. Three worker
entry files without a matching manifest/native closure do not pass readiness.
With configuration, Doctor reuses Host inspection, reads the actual resolved
target descriptor and checks provider-declared exact prerequisite files and
bounded build-tool version probes. Required archive tools depend on output form.
Missing or incompatible tools fail with actionable diagnostics. Doctor does not
compile/link native code, invoke the library or download prerequisites. A custom
provider whose prerequisite discovery cannot be performed by this CLI is
explicitly pending and requires matching Host preflight; it is not silently
treated as a built-in provider.

## Explicit target triples

`targets[].triple` optionally selects the compiler triple; the SDK equivalent
is `AnalysisRequest.triple`. It supplements the target ID and never replaces
its CPU, operating system or ABI. For example, `android-arm64` can select
`aarch64-linux-android26`, while an iOS simulator uses an explicit
`arm64-apple-ios18.0-simulator` triple with its simulator target ID and SDK.

Built-in providers reject incompatible overrides before parsing. GNU and musl,
iOS devices and simulators, Windows MSVC and GNU, and wasm32 Emscripten and WASI
remain distinct. Supported deployment/version tails are retained in reports,
native plans and input identities. Versioned MSVC compatibility overrides are
not accepted through this field; the selected installed toolchain owns that
compatibility profile. Custom target providers define their own triple policy.
The compiler, SDK/sysroot and native binary still require their own checks;
accepting a triple is target resolution rather than platform certification.

## Prebuilt artifacts and packaging

`nativeBuild.mode = "prebuilt"` declares `artifacts` and a concrete
`prebuiltTargetId`. A CPU architecture match alone cannot establish libc,
calling ABI or compiler configuration compatibility. Prebuilt inputs still
require artifact/target/export validation and exact input identities.

`nativeBuild.mode = "none"` omits construction of the configured native product;
it does not imply that all compiler verification is disabled. Parser-adapted C++
facts can still require the independent proof described
[below](#selected-native-compiler-source-view). This proof does not establish
final product linking or invocation. An optional packaging request is
`"packaging": { "enabled": true, "packager": "auto" }`. Native assets use the
target descriptor's platform/CPU/ABI facts; target IDs and NuGet RIDs are not
interchangeable. GNU and musl cannot share an assumed `linux-x64` identity.

Packaging establishes verified asset layout, not clean-install consumption
or runtime invocation. Custom packagers use stable registered IDs and their
own complete input/fingerprint contracts.

## Locked inputs and verification

Generation writes `bridge.manifest.json` as a neutral construction manifest
and `bridge.module.json` as a detached typed C++ model. The manifest includes
the exact target, compilation units, declarations, expected exports and
plugin/input identities needed to reconstruct the request.

Manifests containing selected source facts also require the explicit
`sourceRetainFunctionBodies` frontend option. Candidate and standalone native
verification replay that exact extraction mode. A declaration being a real
definition, including an implicit or defaulted destructor, does not establish
whether its body was retained. Missing or null extraction metadata is rejected;
regenerate the manifest rather than guessing an older file's parse settings.

Source builds capture preflight and compiled header dependencies, effective
source facts, compiler/linker executable bytes, response/configuration files,
link dependencies/probes/traces and search-directory memberships. Effective
declarations and generated native trait/layout guards detect changes that a
matching file name or total struct size cannot prove safe.

Before publication, input closure and artifacts are checked again. Required
symbol inspection and native output-format checks produce evidence with the
executed plan. Unsupported or unprovable situations fail explicitly.
Shared-library checks verify image kind as well as CPU: ELF requires `ET_DYN`,
Mach-O requires a dylib/bundle (including the selected universal slice), and
PE requires `IMAGE_FILE_DLL`. Those checks still do not prove a runtime call.
Native construction caching remains disabled until its complete dependency
identity is proven; generation-stage cache hits do not skip native checks.

The same candidate publication policy applies to native and managed artifacts:
required failures preserve the previous owned complete set. Standalone native
construction uses joint publication when extending an owned generation root.
Planning exposes a declared final path while compiler output stays private.
Neither plan nor export validation claims that an application invoked the ABI.

## Browser workload identity

For browser wasm32, native objects/archives are linked into the application.
Query the **actual consumer's selected .NET 10 SDK**:

```bash
dotnet msbuild Consumer.csproj -nologo -getProperty:EmscriptenSdkToolsPath,EmscriptenCacheSdkCacheDir
```

Use its compiler and matching sysroot, rather than a global unrelated LLVM:

```json
{
  "id": "emscripten-wasm32-emscripten",
  "settings": {
    "compilerPath": "/selected/sdk/tools/bin/clang++",
    "sysRoot": "/selected/cache/tools/emscripten/cache/sysroot",
    "systemIncludeDirectories": ["/selected/cache/tools/emscripten/cache/sysroot/include"],
    "cxxSystemIncludeDirectories": ["/selected/cache/tools/emscripten/cache/sysroot/include/c++/v1"]
  }
}
```

On Windows the driver is `clang++.exe`. Query property values determine actual
paths; example paths above are placeholders, not discovery conventions.
The same compiler/sysroot must serve parser/native construction and consumer
linking. Required workloads are prerequisites, not implicit BGCS downloads.

The generated import identity follows the declared native dependency:
`libapi.a` registers `libapi`; `api.o` registers `api`. A consumer-supplied
`api.c` uses `api`. BGCS records typed `importModuleName`; its MSBuild
integration validates the exact native filename and does not rename it.

## Typed application references

| Kind | Consumption |
| --- | --- |
| `DynamicCopy` | Exact shared-library copy items, including versioned filenames. |
| `BrowserNativeFile` | `NativeFileReference` with matching import identity. |
| `AppleStatic` | `NativeReference` with static/ForceLoad metadata. |
| `NativeAotLibrary` | `NativeLibrary`, requiring the supported NativeAOT consumer profile. |

These choices come from verified output form and runtime/target capabilities,
not filename-extension or target-ID-prefix guessing. Static desktop imports
use the supported `__Internal` adapter profile; browser uses its real native
dependency module identity. [MSBuild](msbuild.md) imports exact manifest items.

See [testing](testing.md), [ABI safety](abi-safety.md) and
[packages](packages.md) for actual independent native, NativeAOT, browser and
installed-product gates. Uncertified platform/provider combinations remain
explicitly pending even when target resolution or plan creation succeeds.

## Selected native compiler source view

The default worker frontend first obtains the resolved MSVC, Clang or GNU
compiler's actual preprocessing view. Native conditionals and builtin
availability select the declarations before Clang extracts detached facts.
`generate` therefore needs the selected compiler and any SDK headers actually
used by its inputs, even when native building is disabled. Self-contained
cross-target preprocessing does not claim that a native linker or target SDK
is available. Custom `IFrontend` implementations receive the resolved request
and remain responsible for their own declared parsing capability.

MSVC requires the conformant
`/Zc:preprocessor` mode and a working `/PD` final macro dump; an explicitly
requested legacy preprocessor is rejected, including through response files.
Parser options without a faithful native mapping are rejected explicitly.
Clang and GNU use separate dependency-tracked source and `-dM` final-macro
operations, preserving comments, source locations, response/configuration
inputs and final `#undef` state. Options that replace the controlled output or
remove its source provenance are rejected, including inside response files.

For GNU C++ headers, the pinned Clang parser cannot consume every GCC-expanded
SDK implementation token. BGCS can reconstruct declarations from complete,
leading SDK include trees authenticated against the selected driver's search
roots, dependency files and system-header markers. It parses those headers in
an isolated declaration prelude while preserving the actual GNU-expanded user
tokens and final native macro state. A user-supplied `-isystem` directory alone
does not authenticate an SDK tree. The current adapter rejects an expanded SDK
tree reached after user semantic tokens, user headers nested inside a folded
SDK tree, unproved leading user pragma/pack/push-pop state, unbalanced SDK pack
state, and macro history that cannot be replayed faithfully. The exact ordered
header entries must also resolve unambiguously. Unsupported concrete GNU
types or evaluations are not replaced with same-size aliases or guessed values.

An isolated prelude is parser compatibility, not an ABI proof. When the frontend
result requires independent native compiler proof (`requiresNativeCompilerProof`),
generation must compile the emitted bridge and canonical/storage assertions
with the selected native compiler and original source settings before publication.
This also applies to `generate`/`bridge` and `nativeBuild.mode` `none` or `prebuilt`;
a cache hit does not waive it. The verification metadata records
`nativeFactProofRequired` and `nativeFactProof`, separately from product build,
final linking and invocation. The proof uses object compilation; its receipt
separately records any object-link operation and reports no final product link
or invocation. Ordinary C requests without this proof requirement do not acquire
an additional C++ compilation requirement; native preprocessing and any explicitly
requested build checks remain separate requirements.

The source view retains original source locations, comments, final macro
values and SDK system-header classification. Ambiguous macro origins remain
unknown. It preserves unsupported dependent intrinsics only while they are
unused; concrete evaluations that Clang cannot prove still fail. Native-build
only definitions remain separate and are checked against the generated facts.

An untouched final macro that exactly matches the executed command-line
definition has declared command-line provenance, reported as information with
an argument digest and no header position. A possible source `#define` or
`#undef`, including one in a dependency, preamble, postamble or inactive branch,
prevents that attribution. Ambiguous origins remain warnings. Native compiler
warnings and notes survive preprocessing in structured diagnostics; console
frontend thresholds affect presentation only. Source-view commands use
`VSLANG=1033` for MSVC or `LC_ALL=C` for GNU/Clang to request stable
diagnostic formatting. MSVC message text can remain localized when only those
resources are installed; its severity, diagnostic code and actual position are
preserved independently. These child-process settings and diagnostics participate in
the observation identity; they do not change the user's native-build environment.

Before returning analysis or publishing generation, BGCS reruns preprocessing
and compares the complete expanded source and final macro state. This detects
absolute and dynamically expanded `__has_include` changes that alter this
source view even when a header dependency list cannot enumerate the queries.
It is not a complete filesystem query trace. A changed source view rejects the candidate
and preserves the previous output. Volatile source inputs such as changing
time macros may consequently require an explicit deterministic definition.
The driver, its reported language frontend, captured source/header and argument
inputs, and tool-directory membership are checked before and after operations
and again before publication. MSVC freezes its complete tool directory;
GNU-style drivers freeze the selected executables and, on Windows, their
sibling DLLs. This does not certify every implicit operating-system dependency.

Detached facts and C# emission can be reused only after fresh preprocessing
establishes the same source tokens, final macros, captured dependencies,
compiler identity and implementation bytes. A cache hit still performs the
required observation replay and every requested build/validation stage.
Only private aggregate filenames in actual line-directive metadata are
canonicalized; a path in a string literal, raw string or expanded
`__BASE_FILE__` remains semantic input. Unsupported reusable inputs continue
to disable the affected cache. Platform certification requires actual
provider-specific execution: Windows Clang/MSVC evidence does not certify
GNU or Apple toolchains on another operating system.

## C header helpers

A `static` or inline function defined in a C header is not necessarily an
exported symbol in the upstream shared library. By default,
`source.includeHeaderHelpers` is `false`: ordinary static, inline and
direct-call-only helpers and their aliases do not become required imports.
This lets bindings load an existing library's external API without rebuilding
it for header helpers. Source analysis retains the original source facts.

Set `source.includeHeaderHelpers` to `true` to request this functionality.
BGCS emits a real C adapter for every selected header helper. The public
C# name remains the original function name; the ABI manifest records its
allocated wrapper export and original declaration identity.

The generated files are `native/include/<prefix>_helpers.h` and
`native/src/<prefix>_helpers.c`, with the mapping in
`native/metadata/header-helpers.manifest.json`. Compile the C source into the
same configured native module as the ordinary C exports. `nativeBuild.mode`
`sources` includes these generated sources automatically. With an external
CMake project, add the manifest's native C source artifacts to that library
target, for example through `target_sources`.

A separately loaded helper DLL does not automatically forward the original
library's exports. The generated API uses one configured module; include both
sets of exports there, or provide an explicit FunctionTable module provider
that owns and routes the required modules. BGCS does not infer such routing.
An exact helper selector or required cleanup names the opt-in when disabled.
Explicit fixed `source.variadicVariants` remain enabled without opting in to
unrelated ordinary header helpers.

`generate` also emits a reusable native build manifest when helpers are
required, even when native building is disabled. A prebuilt library is
accepted only when it already contains the exact wrapper exports. Otherwise
`build` reports the required source file and module, and preserves the
previous complete output. Header helpers with an unspecified or variadic
prototype require an explicit fixed native shim. BGCS reparses the generated
header and checks the wrapper parameter and return types before publishing.

## Native string constant proof

Native construction independently checks selected string constants under the actual
compiler's execution-character options. It uses literal tokens obtained from native
preprocessing, without reparsing C headers as C++ or rebuilding literals from expected
managed bytes. A headerless C++ constexpr invocation checks every code unit, width and
complete length, including embedded and final zero units. The selected compiler must
therefore have the required C++ frontend even when the original header is C17; an
unavailable frontend or unprovable literal form fails explicitly.

The construction receipt's `literalProofs` retains the exact command, complete proof
source and digest, selected constant IDs and completed compiler result after temporary
inputs are deleted. Standalone native construction exposes the same receipts in its SDK
result and directory verification file. These specific proofs do not rewrite source
`Declared` or configuration `UserAsserted` evidence into a general `Verified` claim.
