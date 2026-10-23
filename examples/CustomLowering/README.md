# Custom C value lowering

`MeasurePlugin` references only `BGCS.Core`. It recognizes one explicit native declaration, validates its actual field shape, then supplies a typed integer carrier and fingerprinted input/result conversion expressions. It does not expose parser objects or patch generated source.

Build the project, reference it from your generation tool, and register the plugin:

```csharp
using BGCS;
using BGCS.Examples.CustomLowering;

await using BindingHost host = BindingGenerator.CreateBuilder()
    .AddPlugin<MeasurePlugin>()
    .Build();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"), cancellationToken);
if (!result.success) throw new InvalidOperationException(result.GetDiagnosticSummary());
```

Use this directory's `native.h` and configuration. `EchoMeasure` receives an integer carrier converted to the original C value and returns its `raw` field through the integer carrier. The original record and function identities remain intact. The generated header is reparsed and its complete physical signature checked before C# is emitted.

The consumer compiles the generated C bridge with its implementation of `EchoMeasure`, then compiles the generated C# and supplies the native module. BGCS does not invoke those build steps. More complex ownership, retained pointers or callbacks need their explicit semantic protocols; conversion expressions alone cannot prove lifetimes or a new by-value calling ABI.
