using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Workers;
using System;
using BGCS.Core.Hosting;
using BGCS.Hosting.Standard;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using BGCS.Core.Artifacts;
using BGCS.Core.Versioning;
using BGCS.Infrastructure.Workers;

namespace BGCS.Tool.Commands;

internal static class VersionCommand
{
    internal static int Run(
        string[] args,
        TextWriter output
    ) {
        bool json = args.Length == 1 && args[0] == "--json";
        if (args.Length != 0 && !json) throw new ArgumentException("version accepts only the optional --json argument.");
        using HostServices host = new HostServicesProvider().Create(new(AppContext.BaseDirectory, JsonSerializer.SerializeToElement(new { })));
        Assembly tool = typeof(VersionCommand).Assembly;
        string root = Path.Combine(AppContext.BaseDirectory, "bgcs", "worker");
        string status = "missing";
        string? parserIdentity = null;
        string? parserHost = null;
        if (File.Exists(Path.Combine(root, "BGCS.Frontend.Clang.Worker.dll")))
        {
            _ = WorkerAssetLocator.Locate(host, applicationBase: AppContext.BaseDirectory);
            string manifestPath = Path.Combine(root, "worker-assets.manifest.json");
            status = File.Exists(manifestPath) ? "verified" : "unlocked";
            if (status == "verified")
            {
                WorkerAssetManifest manifest = new WorkerAssetDeployer(host.paths).ReadManifest(manifestPath);
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
            runtimeSourceVersion = Version(typeof(BGCS.CSharp.CSharpBackend).Assembly),
            models = new { native = ModelRevision.C_NATIVE, nativeBridge = ModelRevision.C_NATIVE_BRIDGE },
            frontendProtocol = ModelRevision.C_FRONTEND_PROTOCOL,
            parser = new
            {
                expectedIdentity = ClangParserIdentity.C_IDENTITY,
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
            output.WriteLine($"SDK: {result.sdkVersion}; Contracts: {result.contracts.version} (API {result.contracts.major}.{result.contracts.apiMinor}); Runtime source catalog: {result.runtimeSourceVersion}");
            output.WriteLine($"Models: native={result.models.native}, nativeBridge={result.models.nativeBridge}; frontend protocol={result.frontendProtocol}");
            output.WriteLine($"Parser: {parserIdentity ?? ClangParserIdentity.C_IDENTITY}; asset deployment={status}{(parserHost is null ? "" : "; host=" + parserHost)}");
            output.WriteLine($"Host: {result.hostRid}; .NET: {result.framework}");
        }
        return 0;
    }

    private static string Version(Assembly assembly) => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString() ?? "unknown";
}
