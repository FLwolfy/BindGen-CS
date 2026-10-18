using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;

namespace BGCS;

/// <summary>Executes workspace dependency graphs through independent host generation sessions.</summary>
public sealed partial class BindingHost
{
    /// <summary>Generates projects after their explicit prerequisites and preserves independent successful branches.</summary>
    /// <param name="workspace">The prevalidated frozen project graph.</param>
    /// <param name="cancellationToken">Cancels the complete workspace and all active project sessions.</param>
    /// <returns>Stable project outcomes and overall success.</returns>
    public Task<WorkspaceResult> GenerateWorkspaceAsync(
        BindingWorkspace workspace,
        CancellationToken cancellationToken = default
    )
        => ExecuteWorkspaceAsync(workspace, cancellationToken);

    /// <summary>Compares projects through the same prerequisite graph without replacing formal outputs.</summary>
    /// <param name="workspace">The validated frozen project graph.</param>
    /// <param name="cancellationToken">Cancels queued and active comparisons.</param>
    /// <returns>Complete project comparisons in presentation order.</returns>
    public async Task<WorkspaceDiffResult> DiffWorkspaceAsync(
        BindingWorkspace workspace,
        CancellationToken cancellationToken = default
    ) {
        ImmutableArray<WorkspaceExecution<GenerationDiffResult>> results = await ExecuteWorkspaceCoreAsync(workspace,
            (
                project,
                token
            ) => DiffAsync(project.request, token), result => result.success, result => result.diagnostics, cancellationToken).ConfigureAwait(false);
        return new(results.Select(result => new WorkspaceDiffProjectResult(result.projectId, result.status, result.result, result.diagnostics)).ToImmutableArray());
    }

    private async Task<WorkspaceResult> ExecuteWorkspaceAsync(
        BindingWorkspace workspace,
        CancellationToken cancellationToken
    ) {
        ImmutableArray<WorkspaceExecution<GenerationResult>> results = await ExecuteWorkspaceCoreAsync(workspace,
            (
                project,
                token
            ) => GenerateAsync(project.request, token), result => result.success, result => result.diagnostics, cancellationToken).ConfigureAwait(false);
        return new(results.Select(result => new WorkspaceProjectResult(result.projectId, result.status, result.result, result.diagnostics)).ToImmutableArray());
    }

    private sealed record WorkspaceExecution<T>(
        string projectId,
        WorkspaceProjectStatus status,
        T? result,
        ImmutableArray<BindingDiagnostic> diagnostics
    ) where T : class
    {
        internal bool success => status == WorkspaceProjectStatus.Completed;
    }

    private async Task<ImmutableArray<WorkspaceExecution<T>>> ExecuteWorkspaceCoreAsync<T>(
        BindingWorkspace workspace,
        Func<WorkspaceProject, CancellationToken, Task<T>> execute,
        Func<T, bool> isSuccessful,
        Func<T, ImmutableArray<BindingDiagnostic>> getDiagnostics,
        CancellationToken cancellationToken
    ) where T : class
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_closing, this);
            m_sessions++;
        }
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
        using SemaphoreSlim capacity = new(workspace.parallelism);
        Dictionary<string, Task<WorkspaceExecution<T>>> tasks = new(StringComparer.Ordinal);
        try
        {
            foreach (WorkspaceProject project in workspace.executionOrder)
            {
                Task<WorkspaceExecution<T>>[] prerequisites = project.dependencies.Select(id => tasks[id]).ToArray();
                tasks.Add(project.id, ExecuteProjectAsync(project, prerequisites));
            }
            await Task.WhenAll(tasks.Values).ConfigureAwait(false);
            return workspace.projects.Select(project => tasks[project.id].GetAwaiter().GetResult()).ToImmutableArray();
        }
        finally
        {
            lock (m_gate)
                if (--m_sessions == 0 && m_closing) m_drained.TrySetResult();
        }

        async Task<WorkspaceExecution<T>> ExecuteProjectAsync(
            WorkspaceProject project,
            Task<WorkspaceExecution<T>>[] prerequisites
        ) {
            WorkspaceExecution<T>[] previous = await Task.WhenAll(prerequisites).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            string[] failures = previous.Where(result => !result.success).Select(result => result.projectId).Order(StringComparer.Ordinal).ToArray();
            if (failures.Length != 0)
                return new(project.id, WorkspaceProjectStatus.Skipped, null,
                    [new(BindingDiagnosticSeverity.Error, $"Workspace project '{project.id}' was skipped because prerequisites failed: {string.Join(", ", failures)}.", "BGCS-WORKSPACE-DEPENDENCY")]);
            await capacity.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                T result = await execute(project, cancellation.Token).ConfigureAwait(false);
                return new(project.id, isSuccessful(result) ? WorkspaceProjectStatus.Completed : WorkspaceProjectStatus.Failed, result, getDiagnostics(result));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception failure)
            {
                return new(project.id, WorkspaceProjectStatus.Failed, null,
                    [new(BindingDiagnosticSeverity.Error, $"Workspace project '{project.id}' failed: {failure.Message}", "BGCS-WORKSPACE-PROJECT")]);
            }
            finally { capacity.Release(); }
        }
    }
}
