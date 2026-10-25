# BGCS.Extension.BrowserWasm

Explicit Emscripten wasm32 target, Mono Wasm execution profile and scalar/pointer
ABI providers. Register `BrowserWasmPlugin` in your generation tool, or load its
local manifest through the CLI. BGCS generates source; your application compiles
and links it with its selected workload.

Use `api.runtime = "dotnet.mono-wasm"` and explicitly select `libraryImport` or
`dllImport`. FunctionTable is rejected: a function address does not establish
the trimmed interpreter's indirect-call signature table. The same admission rule
applies when the consumer later chooses AOT. No unused imports are synthesized.

Aggregate values use the existing pointer adapters. Callback lifetimes and
nonblocking cleanup still require the declared native protocol. The repository
fixture tests the admitted imports in trimmed interpreter and AOT applications;
this does not certify every browser, SDK version or arbitrary signature.

Target fingerprints use the public extension protocol and the existing frontend
input snapshots. Compiler/SDK metadata, headers, search roots, macros, language
and parser arguments participate in invalidation. Missing/incomplete third-party
fingerprints continue to disable affected cache reuse.
