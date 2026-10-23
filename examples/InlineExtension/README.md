# InlineExtension

Implement a neutral interface directly in the generation tool, without a plugin package or manifest.

From the repository root:

```bash
dotnet run --project examples/InlineExtension -- examples/InlineExtension/bindgen.json
```

The repository project reference is for developing this example. In your own tool, reference `BGCS` 4.0.0 instead. `BGCS.Core` supplies the interfaces and immutable models.

The program emits source; your application separately compiles it and provides the native implementation. It does not invoke a consumer build.

This small implementation intentionally has no complete cache fingerprint. Generation remains valid; affected cache reuse is disabled. A reusable extension should implement `ICacheFingerprintProvider` and declare every external input it reads.
