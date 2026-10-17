using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        if (arguments is [string schemaKind, string destination] && schemaKind is "--schema" or "--plugin-schema")
        {
            string absolute = Path.GetFullPath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            BGCS.Configuration.SchemaCatalog catalog = new();
            string schema = (schemaKind == "--plugin-schema" ? catalog.CreatePluginSchema() : catalog.CreateBindingSchema()).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
            await File.WriteAllTextAsync(absolute, schema, new UTF8Encoding(false));
            return 0;
        }
        Stopwatch elapsed = Stopwatch.StartNew();
        ReportPhase("entry");
        GenerationResult result;
        bool frontendLoaded;
        await using (BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(arguments[0]).Build())
        {
            ReportPhase("host-built");
            GenerationRequest request = GenerationRequest.FromFile(arguments[1]);
            ReportPhase("request-loaded");
            ReportPhase("generation-start");
            result = await host.GenerateAsync(request);
            ReportPhase("generation-completed");
            frontendLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "BGCS.Frontend.Clang");
        }
        ReportPhase("dispose-completed");
        Console.WriteLine(JsonSerializer.Serialize(new { result.success, result.outputRoot, frontendLoaded, diagnosticSummary = result.GetDiagnosticSummary() }));
        return result.success && !frontendLoaded ? 0 : 1;

        void ReportPhase(string phase)
        {
            Console.Error.WriteLine($"Embedded SDK host phase={phase}, elapsed={elapsed.Elapsed}.");
            Console.Error.Flush();
        }
    }
}
