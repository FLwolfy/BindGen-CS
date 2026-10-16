using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Versioning;
using BGCS.Infrastructure.Workers;
using BGCS.Runtime;

namespace BGCS.Tool.Commands;

internal static class VersionCommand
{
    internal static int Run(
        string[] args,
        TextWriter output
    ) {
        bool json = args.Length == 1 && args[0] == "--json";
        if (args.Length != 0 && !json) throw new ArgumentException("version accepts only the optional --json argument.");
        Assembly tool = typeof(VersionCommand).Assembly;
        string root = Path.Combine(AppContext.BaseDirectory, "bgcs", "worker");
        string status = "missing";
        string? parserIdentity = null;
        string? parserHost = null;
        if (File.Exists(Path.Combine(root, "BGCS.Frontend.Clang.Worker.dll")))
        {
            _ = WorkerAssetLocator.Locate(applicationBase: AppContext.BaseDirectory);
            string manifestPath = Path.Combine(root, "worker-assets.manifest.json");
            status = File.Exists(manifestPath) ? "verified" : "unlocked";
            if (status == "verified")
            {
                WorkerAssetManifest manifest = new WorkerAssetDeployer().ReadManifest(manifestPath);
                parserIdentity = manifest.parserIdentity;
                parserHost = manifest.hostRid;
            }
        }
        var result = new
        {
            product = "BindGen-CS",
            version = Version(tool),
            sdkVersion = Version(typeof(BindingHost).Assembly),
            contracts = new { version = Version(typeof(ContractVersion).Assembly), major = ContractVersion.C_MAJOR, apiMinor = ContractVersion.C_API_MINOR },
            runtimeVersion = Version(typeof(NativeModuleSession).Assembly),
            models = new { native = ModelRevision.C_NATIVE, cppBridge = ModelRevision.C_CPP_BRIDGE },
            frontendProtocol = ModelRevision.C_FRONTEND_PROTOCOL,
            parser = new
            {
                expectedIdentity = WorkerProtocol.C_PARSER_IDENTITY,
                deployedIdentity = parserIdentity,
                hostRid = parserHost,
                status,
                assetDigestsVerified = status == "verified",
                parseExecuted = false
            },
            hostRid = RuntimeInformation.RuntimeIdentifier,
            framework = Environment.Version.ToString()
        };
        if (json) output.WriteLine(JsonSerializer.Serialize(result, GenerateCommand.JsonOptions));
        else
        {
            output.WriteLine($"{result.product} {result.version}");
            output.WriteLine($"SDK: {result.sdkVersion}; Contracts: {result.contracts.version} (API {result.contracts.major}.{result.contracts.apiMinor}); Runtime: {result.runtimeVersion}");
            output.WriteLine($"Models: native={result.models.native}, cppBridge={result.models.cppBridge}; frontend protocol={result.frontendProtocol}");
            output.WriteLine($"Parser: {parserIdentity ?? WorkerProtocol.C_PARSER_IDENTITY}; asset deployment={status}{(parserHost is null ? "" : "; host=" + parserHost)}");
            output.WriteLine($"Host: {result.hostRid}; .NET: {result.framework}");
        }
        return 0;
    }

    private static string Version(Assembly assembly) => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString() ?? "unknown";
}
