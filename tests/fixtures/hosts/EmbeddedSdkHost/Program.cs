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
        if (arguments is ["--inspect-and-diff", string inspectionWorker, string input])
        {
            await using BindingHost inspectionHost = CreateHost(inspectionWorker);
            GenerationRequest request = GenerationRequest.FromFile(input);
            GenerationResult generated = await inspectionHost.GenerateAsync(request);
            if (!generated.success) throw new InvalidOperationException(generated.GetDiagnosticSummary());
            var previous = Directory.GetFiles(generated.outputRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            InspectionResult inspection = await inspectionHost.InspectAsync(request);
            GenerationDiffResult difference = await inspectionHost.DiffAsync(request);
            bool unchanged = previous.Count == Directory.GetFiles(generated.outputRoot, "*", SearchOption.AllDirectories).Length
                && previous.All(file => file.Value.SequenceEqual(File.ReadAllBytes(file.Key)));
            bool success = inspection.success && difference.success && !difference.hasChanges && unchanged;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success, tempRoot = Path.GetTempPath(), inspectionSucceeded = inspection.success,
                differenceSucceeded = difference.success, difference.hasChanges, formalOutputUnchanged = unchanged,
                diagnostics = inspection.diagnostics.Concat(difference.diagnostics)
            }));
            return success ? 0 : 1;
        }
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
        bool nativeParserLoaded;
        await using (BindingHost host = CreateHost(arguments[0]))
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

    private static BindingHost CreateHost(string worker)
    {
        const string identity = "bgcs.frontend.clang.worker";
        return BindingGenerator.CreateBuilder().ReplaceFactory<IFrontend>(identity, new(identity, ExtensionPort.Frontend), context =>
            new FrontendWorkerClient(WorkerAssetLocator.Locate(context.RequireHostServices(), worker), context.RequireHostServices(), TimeSpan.FromSeconds(300))).Build();
    }
}
