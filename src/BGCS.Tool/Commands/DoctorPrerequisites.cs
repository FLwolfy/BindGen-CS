using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Workers;
using System;
using System.Text.Json;
using BGCS.Core.Hosting;
using BGCS.Hosting.Standard;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Artifacts;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.Workers;
using BGCS.Targets.Desktop.Capabilities;

namespace BGCS.Tool.Commands;

internal static class DoctorPrerequisites
{
    internal sealed record WorkerReadiness(
        bool ready,
        string status,
        string root,
        string? hostRid,
        string? parserIdentity,
        ImmutableArray<BindingDiagnostic> diagnostics
    )
    {
        public bool assetDigestsVerified => ready;
        public bool parseExecuted => false;
    }

    internal static WorkerReadiness InspectWorker(string applicationBase)
    {
        using HostServices host = new HostServicesProvider().Create(new(Path.GetFullPath(applicationBase), JsonSerializer.SerializeToElement(new { })));
        string root = Path.GetFullPath(Path.Combine(applicationBase, "bgcs", "worker"));
        string manifestPath = Path.Combine(root, "worker-assets.manifest.json");
        if (!File.Exists(manifestPath))
            return new(false, Directory.Exists(root) ? "unlocked" : "missing", root, null, null,
                [new(BindingDiagnosticSeverity.Error,
                    "A complete locked application-deployed worker is required. Restore/deploy the BGCS SDK assets or reinstall the host-specific tool; three entry files alone do not establish parser readiness.",
                    "BGCS-DOCTOR-WORKER")]);
        try
        {
            WorkerAssetDeployer deployer = new(host.paths);
            WorkerAssetManifest manifest = deployer.ReadManifest(manifestPath);
            deployer.VerifyDeployment(root, manifest, host.execution.hostRuntimeIdentifier);
            return new(true, "verified", root, manifest.hostRid, manifest.parserIdentity, []);
        }
        catch (Exception failure)
        {
            return new(false, "invalid", root, null, null,
                [new(BindingDiagnosticSeverity.Error,
                    $"The deployed parser closure is invalid: {failure.Message} Restore the exact matching host assets before parsing.",
                    "BGCS-DOCTOR-WORKER")]);
        }
    }

}
