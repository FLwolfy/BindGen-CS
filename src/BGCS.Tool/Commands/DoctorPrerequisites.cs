using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms.Capabilities;

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
        string root = Path.GetFullPath(Path.Combine(applicationBase, "bgcs", "worker"));
        string manifestPath = Path.Combine(root, "worker-assets.manifest.json");
        if (!File.Exists(manifestPath))
            return new(false, Directory.Exists(root) ? "unlocked" : "missing", root, null, null,
                [new(BindingDiagnosticSeverity.Error,
                    "A complete locked application-deployed worker is required. Restore/deploy the BGCS SDK assets or reinstall the host-specific tool; three entry files alone do not establish parser readiness.",
                    "BGCS-DOCTOR-WORKER")]);
        try
        {
            WorkerAssetDeployer deployer = new();
            WorkerAssetManifest manifest = deployer.ReadManifest(manifestPath);
            deployer.VerifyDeployment(root, manifest, RuntimeInformation.RuntimeIdentifier);
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
