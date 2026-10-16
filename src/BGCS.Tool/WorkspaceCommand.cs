using System;
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
        if (positional.Length != 2 || positional[0] is not ("validate" or "generate" or "build" or "diff"))
            throw new ArgumentException("workspace expects '<validate|generate|build|diff> <workspace.json> [--json]'.");
        using CommandCancellation cancellation = new();
        return ExecuteAsync().GetAwaiter().GetResult();

        async System.Threading.Tasks.Task<int> ExecuteAsync()
        {
            BindingWorkspace workspace = await new WorkspaceLoader().LoadAsync(positional[1], cancellation.token).ConfigureAwait(false);
            await using BindingHost host = BindingHost.CreateDefault();
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
            WorkspaceResult generated = positional[0] == "build"
                ? await host.BuildWorkspaceAsync(workspace, cancellation.token).ConfigureAwait(false)
                : await host.GenerateWorkspaceAsync(workspace, cancellation.token).ConfigureAwait(false);
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
