using System;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using BGCS.Core.Inputs;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Plugins;
using BGCS.Core.Targets;
using BGCS.Targets.Desktop.Targeting;
using BGCS.Targets.Desktop.Toolchains;
using System.Collections.Immutable;
using System.Linq;

namespace BGCS.Targets.Desktop;

/// <summary>Explicitly installs the maintained target families and their read-only SDK discovery rules.</summary>
public sealed class DesktopTargetsExtension : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc/>
    public PluginDescriptor descriptor => new("bgcs.targets.desktop", "4.0.0", [new("bgcs.targets.desktop.standard", ExtensionPort.NativeTargetProvider)]);

    /// <inheritdoc/>
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configuration);
        registry.RegisterFactory<INativeTargetProvider>("bgcs.targets.desktop.standard", _ => new DesktopTargetProvider());
    }

    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("bgcs-target-profiles:4")))), []);
    }
}

/// <summary>Resolves a maintained target family and freezes its applicable SDK header facts without executing a compiler.</summary>
public sealed class DesktopTargetProvider : INativeTargetProvider, ICacheFingerprintProvider, IExternalInputProvider
{
    /// <inheritdoc/>
    public bool TryResolve(
        NativeTargetRequest request,
        [NotNullWhen(true)] out NativeTargetDescriptor? target
    ) {
        ArgumentNullException.ThrowIfNull(request);
        if (!new TargetResolver().TryResolve(request, out NativeTargetDescriptor? resolved))
        {
            target = null;
            return false;
        }
        resolved = ToolchainResolver.ResolveToolchain(resolved, request.language);
        target = new(resolved.targetId, resolved.platformId, resolved.architectureId, resolved.abiId,
            resolved.triple, resolved.toolchain, resolved.capabilities, resolved.byteOrder)
        {
            headerSearchPaths = ToolchainResolver.ResolveHeaderSearchPaths(resolved, request.language).ToImmutableArray(),
            sourceConventions = resolved.platformId == "windows"
                ? new("__declspec(dllexport)", "__cdecl", new(resolved.architectureId == "x64" ? "Win64" : "C", true))
                : new("__attribute__((visibility(\"default\")))", "", new(resolved.architectureId == "x64" ? "X86_64SysV" : "C", false))
        };
        return true;
    }

    /// <inheritdoc/>
    public ImmutableArray<DeclaredExternalInput> GetExternalInputs(
        ExternalInputContext context,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(context);
        List<DeclaredExternalInput> inputs = [];
        bool explicitCompiler = false, explicitSdk = false;
        foreach (JsonElement settings in Settings(context.configuration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.TryGetProperty("compilerPath", out JsonElement compiler) && compiler.ValueKind == JsonValueKind.String)
            {
                explicitCompiler = true;
                string value = compiler.GetString()!;
                if (Path.IsPathFullyQualified(value) || value.Contains('/') || value.Contains('\\'))
                    inputs.Add(new(Path.GetFullPath(value, context.pathBase), ExternalInputKind.File, allowMissing: true));
            }
            if (settings.TryGetProperty("sysRoot", out JsonElement sysroot) && sysroot.ValueKind == JsonValueKind.String)
            {
                explicitSdk = true;
                AddAppleMetadata(Path.GetFullPath(sysroot.GetString()!, context.pathBase));
            }
            foreach (string name in new[] { "systemIncludeDirectories", "cxxSystemIncludeDirectories", "systemIncludeFolders", "cxxSystemIncludeFolders" })
                if (settings.TryGetProperty(name, out JsonElement paths) && paths.ValueKind == JsonValueKind.Array && paths.GetArrayLength() != 0) explicitSdk = true;
        }
        if (!explicitSdk && Environment.GetEnvironmentVariable("SDKROOT") is string selected && !string.IsNullOrWhiteSpace(selected))
            AddAppleMetadata(Path.GetFullPath(selected, context.pathBase));
        if (!explicitCompiler && !explicitSdk && OperatingSystem.IsWindows())
        {
            string architecture = RuntimeInformation.ProcessArchitecture switch { Architecture.X86 => "x86", Architecture.Arm64 => "arm64", _ => "x64" };
            if (MsvcToolchainDiscovery.FindCompiler(architecture) is string compiler) inputs.Add(new(compiler, ExternalInputKind.File));
        }
        return inputs.Distinct().ToImmutableArray();

        void AddAppleMetadata(string root)
        {
            inputs.Add(new(Path.Combine(root, "SDKSettings.json"), ExternalInputKind.File, allowMissing: true));
            inputs.Add(new(Path.Combine(root, "SDKSettings.plist"), ExternalInputKind.File, allowMissing: true));
        }
        static IEnumerable<JsonElement> Settings(JsonElement configuration)
        {
            if (configuration.ValueKind != JsonValueKind.Object) yield break;
            if (configuration.TryGetProperty("toolchain", out JsonElement toolchain) && toolchain.ValueKind == JsonValueKind.Object) yield return toolchain;
            if (configuration.TryGetProperty("targets", out JsonElement targets) && targets.ValueKind == JsonValueKind.Array)
                foreach (JsonElement target in targets.EnumerateArray())
                    if (target.ValueKind == JsonValueKind.Object && target.TryGetProperty("settings", out JsonElement settings) && settings.ValueKind == JsonValueKind.Object) yield return settings;
        }
    }

    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        List<DeclaredExternalInput> metadata = [];
        if (context.target.toolchain.compilerPath is string compiler && Path.IsPathFullyQualified(compiler))
            metadata.Add(new(compiler, ExternalInputKind.File, allowMissing: true));
        if (context.target.toolchain.sysRoot is string root && context.target.platformId == "macos")
        {
            metadata.Add(new(Path.Combine(root, "SDKSettings.json"), ExternalInputKind.File, allowMissing: true));
            metadata.Add(new(Path.Combine(root, "SDKSettings.plist"), ExternalInputKind.File, allowMissing: true));
        }
        return new(new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("bgcs-desktop-target-provider:4")))), metadata.ToImmutableArray());
    }
}
