using System;
using System.Linq;
using System.Collections.Immutable;
using System.Threading;
using BGCS;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: InlineExtension <bindgen.json>");
    return 2;
}
await using BindingHost generator = BindingGenerator.CreateBuilder()
    .RegisterFactory<INativeModuleTransform>(new("sample.documentation", ExtensionPort.NativeModuleTransform), _ => new SampleRules())
    .Build();
GenerationResult result = await generator.GenerateAsync(GenerationRequest.FromFile(args[0]));
foreach (var diagnostic in result.diagnostics)
    Console.Error.WriteLine($"{diagnostic.severity}: {diagnostic.code}: {diagnostic.message}");
return result.success ? 0 : 1;

internal sealed class SampleRules : INativeModuleTransform
{
    public NativeModule Transform(NativeModule module, AnalysisContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new NativeModule(module.targetId, module.triple, module.pointerSize, module.types,
            module.declarations.Select(declaration => declaration.kind == "Function" && declaration.documentation is null
                ? declaration with { documentation = "Native operation exposed through this binding." } : declaration).ToImmutableArray(), module.diagnostics)
        { language = module.language, byteOrder = module.byteOrder };
    }
}
