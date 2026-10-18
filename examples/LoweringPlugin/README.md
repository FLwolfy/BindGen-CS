# Contracts-only callable lowering plugin

From this directory:

```bash
dotnet build BGCS.Example.LoweringPlugin.csproj --configuration Release
python write-manifest.py
bindgen-cs generate bindgen.json
```

Compile the emitted bridge sources and `native.hpp` with your native build system,
then compile the generated C# in a consumer referencing `BGCS.Runtime`. The generator
does not compile, link or deploy either consumer. See [external compilation](../../docs/native-metadata.md#external-compilation).

The plugin references only `BGCS.Contracts`. The repository project reference can be
replaced with a `PackageReference` to `BGCS.Contracts` version `3.0.0` after release.
It reads detached `NativeDeclaration` facts and matches the exact nonstatic
`void Engine::World::Tick()` signature. It returns the complete typed instance action,
opaque receiver representation and stable `engine_world_tick` export. The ordinary
bridge emitter supplies the checked pointer, status and exception boundary.
Other signatures keep the ordinary planner behavior.

`write-manifest.py` records the actual assembly and dependency manifest hashes.
Regenerate `plugin.json` after every rebuild; the loader rejects changed executable
bytes instead of activating an unrecorded implementation. The configuration loads that
manifest through `extensions.plugins`. The plugin and its service each provide a
fingerprint for their private constant state; assembly content is independently locked
by the manifest. Session-owned factories avoid mutable cross-request state.

The example contains no frontend, Host implementation or C++ emitter references.
Generated constructors and release exports retain their ordinary names; only the
selected member uses this custom ABI name. Inspect `bridge.module.json` and the
reparsed ABI metadata to review the resulting plan before consumption.
