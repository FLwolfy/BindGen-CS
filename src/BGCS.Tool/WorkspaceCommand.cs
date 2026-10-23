using System;
using System.IO;
using BGCS.Core.Hosting;
using BGCS.Hosting.Standard;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using BGCS.Tool.Commands;

namespace BGCS.Tool;

internal static class WorkspaceCommand
{
    internal static int Run(string[] args)
    {
        args = DiagnosticPresentation.Extract(args, out DiagnosticPresentation.Options diagnostics);
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string[] positional = args.Where(argument => argument != "--json").ToArray();
        if (positional.Length != 2 || positional[0] is not ("validate" or "generate" or "diff"))
            throw new ArgumentException("workspace expects '<validate|generate|diff> <workspace.json> [--json]'.");
        using CommandCancellation cancellation = new();
        return ExecuteAsync().GetAwaiter().GetResult();

        async System.Threading.Tasks.Task<int> ExecuteAsync()
        {
            await using HostServices services = new HostServicesProvider().Create(new(Path.GetDirectoryName(Path.GetFullPath(positional[1]))!, JsonSerializer.SerializeToElement(new { })));
            BindingWorkspace workspace = await new WorkspaceLoader(services.paths).LoadAsync(positional[1], cancellation.token).ConfigureAwait(false);
            await using BindingHost host = BindingGenerator.CreateDefault();
            if (positional[0] == "diff")
            {
                WorkspaceDiffResult result = await host.DiffWorkspaceAsync(workspace, cancellation.token).ConfigureAwait(false);
                cancellation.token.ThrowIfCancellationRequested();
                GenerateCommand.WriteDiagnostics(result.diagnostics, diagnostics);
                if (json) Console.WriteLine(JsonSerializer.Serialize(result, GenerateCommand.JsonOptions));
                else foreach (var project in result.projects)
                {
                    Console.WriteLine($"{project.projectId}: {project.status}");
                    if (project.diff is not null)
                        foreach (ArtifactChange change in project.diff.changes) Console.WriteLine($"{project.projectId}: {change.kind}: {change.relativePath}");
                }
                return !result.success ? 2 : result.hasChanges ? 1 : 0;
            }
            if (positional[0] == "validate")
            {
                bool success = true;
                List<WorkspaceValidationProject> projects = [];
                foreach (WorkspaceProject project in workspace.projects)
                {
                    InspectionResult result = await host.InspectAsync(project.request, cancellation.token).ConfigureAwait(false);
                    cancellation.token.ThrowIfCancellationRequested();
                    GenerateCommand.WriteDiagnostics(result.diagnostics, diagnostics);
                    projects.Add(new(project.id, result));
                    if (!json) Console.WriteLine($"{project.id}: {(result.success ? "validated" : "failed")}");
                    success &= result.success;
                }
                if (json) Console.WriteLine(JsonSerializer.Serialize(new { success, projects }, GenerateCommand.JsonOptions));
                return success ? 0 : 1;
            }
            WorkspaceResult generated = await host.GenerateWorkspaceAsync(workspace, cancellation.token).ConfigureAwait(false);
            cancellation.token.ThrowIfCancellationRequested();
            GenerateCommand.WriteDiagnostics(generated.diagnostics, diagnostics);
            if (json) Console.WriteLine(JsonSerializer.Serialize(generated, GenerateCommand.JsonOptions));
            else foreach (WorkspaceProjectResult project in generated.projects) Console.WriteLine($"{project.projectId}: {project.status}");
            return generated.success ? 0 : 1;
        }
    }

    private sealed record WorkspaceValidationProject(
        string projectId,
        InspectionResult result
    );
}
