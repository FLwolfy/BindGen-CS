using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using BGCS.Contracts;

namespace BGCS.Tool.Commands;

internal static class DoctorCommand
{
    internal static int Run(string[] args)
    {
        GenerationArguments.Options options = GenerationArguments.ParseOptions(args);
        DoctorPrerequisites.WorkerReadiness worker = DoctorPrerequisites.InspectWorker(AppContext.BaseDirectory);
        bool configurationPresent = File.Exists(options.configPath);
        if (!configurationPresent)
        {
            ImmutableArray<BindingDiagnostic> diagnostics = worker.diagnostics;
            if (options.configurationExplicit || options.profile is not null || options.target is not null)
                diagnostics = diagnostics.Add(new(BindingDiagnosticSeverity.Error,
                    $"The requested configuration '{Path.GetFullPath(options.configPath)}' is missing. Supply an existing BGCS 3.0 bindgen.json.",
                    "BGCS-DOCTOR-CONFIGURATION"));
            bool success = worker.ready && Environment.Version.Major >= 10 && !diagnostics.Any(item => item.isFailure);
            if (options.json)
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    success,
                    host = RuntimeInformation.RuntimeIdentifier,
                    framework = Environment.Version.ToString(),
                    worker,
                    inspectionExecuted = false,
                    generationExecuted = false,
                    targetValidated = false,
                    diagnostics
                }, GenerateCommand.JsonOptions));
            else
            {
                WriteHost(worker);
                Console.WriteLine("Target SDK/compiler prerequisites have not been inspected; supply bindgen.json to inspect its captured configuration.");
                GenerateCommand.WriteDiagnostics(diagnostics, options.diagnostics);
            }
            return success ? 0 : 1;
        }
        using CommandCancellation cancellation = new();
        return DiagnoseAsync().GetAwaiter().GetResult();

        async System.Threading.Tasks.Task<int> DiagnoseAsync()
        {
            GenerationRequest request = await GenerationArguments.CreateRequestAsync(options, cancellation.token).ConfigureAwait(false);
            if (!worker.ready)
            {
                if (options.json)
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        success = false,
                        host = RuntimeInformation.RuntimeIdentifier,
                        framework = Environment.Version.ToString(),
                        worker,
                        inspectionExecuted = false,
                        generationExecuted = false,
                        targetValidated = false,
                        diagnostics = worker.diagnostics
                    }, GenerateCommand.JsonOptions));
                else
                {
                    WriteHost(worker);
                    GenerateCommand.WriteDiagnostics(worker.diagnostics, options.diagnostics);
                }
                return 1;
            }
            await using BindingHost host = BindingHost.CreateDefault();
            InspectionResult inspection = await host.InspectAsync(request, cancellation.token).ConfigureAwait(false);
            cancellation.token.ThrowIfCancellationRequested();
            ImmutableArray<BindingDiagnostic> diagnostics = worker.diagnostics.AddRange(inspection.diagnostics);
            bool success = inspection.success && !diagnostics.Any(item => item.isFailure);
            if (options.json)
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    success,
                    host = RuntimeInformation.RuntimeIdentifier,
                    framework = Environment.Version.ToString(),
                    worker,
                    inspectionExecuted = true,
                    generationExecuted = false,
                    targetValidated = inspection.success,
                    inspection,
                    diagnostics
                }, GenerateCommand.JsonOptions));
            else
            {
                WriteHost(worker);
                foreach (TargetInspection target in inspection.targets)
                    Console.WriteLine($"Inspected target: {target.targetId} ({target.native.triple}); native compilation and invocation remain the caller's responsibility.");
                GenerateCommand.WriteDiagnostics(diagnostics, options.diagnostics);
            }
            return success ? 0 : 1;
        }
    }

    private static void WriteHost(DoctorPrerequisites.WorkerReadiness worker)
    {
        Console.WriteLine($"Host: {RuntimeInformation.RuntimeIdentifier}");
        Console.WriteLine($".NET: {Environment.Version}");
        Console.WriteLine($"Parser assets: {worker.status} ({worker.root}); verification does not execute native parsing.");
    }
}
