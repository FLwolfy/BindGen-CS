# Small C API

This example generates a C# binding for `bgcs_add` through the same CLI used by consumers.
Run the commands from this directory with the repository's .NET SDK available:

```sh
dotnet run --project ../../src/BGCS.Tool -c Release -- generate bindgen.json
dotnet run --project ../../src/BGCS.Tool -c Release -- build bindgen.json
```

`Generated/Bindings.cs` references the independently packaged `BGCS.Runtime` library.
Choose `standalone.json` when the consumer needs the runtime as generated source:

```sh
dotnet run --project ../../src/BGCS.Tool -c Release -- build standalone.json
```

That configuration emits `Bindings.cs` and `Runtime.cs`. The `build` command checks C#
compilation; it does not build `quickstart` or prove native invocation. See
[independent invocation tests](../../docs/testing.md) for actual native calls.
