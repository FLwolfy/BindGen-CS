# EmbeddedGeneration

Call the complete generator as an ordinary C# library.

From the repository root:

```bash
dotnet run --project examples/EmbeddedGeneration -- examples/EmbeddedGeneration/bindgen.json
```

The repository project reference is for developing this example. In your own tool, reference `BGCS` 4.0.0 instead. `BGCS.Core` supplies the interfaces and immutable models.

The program emits source; your application separately compiles it and provides the native implementation. It does not invoke a consumer build.
