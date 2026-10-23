using System;
using BGCS;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: EmbeddedGeneration <bindgen.json>");
    return 2;
}
await using BindingHost generator = BindingGenerator.CreateDefault();
GenerationResult result = await generator.GenerateAsync(GenerationRequest.FromFile(args[0]));
foreach (var diagnostic in result.diagnostics)
    Console.Error.WriteLine($"{diagnostic.severity}: {diagnostic.code}: {diagnostic.message}");
Console.WriteLine(result.outputRoot);
return result.success ? 0 : 1;
