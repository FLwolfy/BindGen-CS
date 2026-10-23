using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS;
using BGCS.Core.Ports;
using BGCS.Core.Plugins;
using BGCS.Frontend.Clang.Workers;

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
        const string newId = "bgcs.frontend.clang.worker";
        bool nativeParserLoaded;
        await using (BindingHost host = BindingGenerator.CreateBuilder().ReplaceFactory<IFrontend>(newId, new(newId, ExtensionPort.Frontend), context =>
            new FrontendWorkerClient(WorkerAssetLocator.Locate(context.RequireHostServices(), arguments[0]), context.RequireHostServices(), TimeSpan.FromSeconds(300))).Build())
        {
            ReportPhase("host-built");
            GenerationRequest request = GenerationRequest.FromFile(arguments[1]);
            ReportPhase("request-loaded");
            ReportPhase("generation-start");
            result = await host.GenerateAsync(request);
            ReportPhase("generation-completed");
            using Process current = Process.GetCurrentProcess();
            nativeParserLoaded = current.Modules.Cast<ProcessModule>().Any(module => Path.GetFileName(module.FileName).StartsWith("libclang", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(module.FileName).StartsWith("libClangSharp", StringComparison.OrdinalIgnoreCase));
        }
        ReportPhase("dispose-completed");
        Console.WriteLine(JsonSerializer.Serialize(new { result.success, result.outputRoot, nativeParserLoaded, diagnosticSummary = result.GetDiagnosticSummary() }));
        return result.success && !nativeParserLoaded ? 0 : 1;

        void ReportPhase(string phase)
        {
            Console.Error.WriteLine($"Embedded SDK host phase={phase}, elapsed={elapsed.Elapsed}.");
            Console.Error.Flush();
        }
    }
}
