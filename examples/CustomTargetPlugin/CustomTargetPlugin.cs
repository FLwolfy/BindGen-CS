using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Targets;

namespace BGCS.Examples.CustomTargetPlugin;

/// <summary>Maps one private target ID to explicitly declared real Clang ABI and SDK inputs.</summary>
public sealed class CustomTargetPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public PluginDescriptor descriptor => new("bgcs.examples.custom-target", "4.1.1", [new("example.custom-target", ExtensionPort.NativeTargetProvider)])
    {
        configurationSchema = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","additionalProperties":false,"required":["profile"],"properties":{"profile":{"type":"string","minLength":1}}}
            """)
    };

    /// <inheritdoc />
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        string profile = configuration.ResolveInputPath("/profile");
        registry.RegisterFactory<INativeTargetProvider>("example.custom-target", _ => new Provider(profile));
    }

    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("custom-target-recipe:1")))), []);
    }

    private sealed class Provider(string profilePath) : INativeTargetProvider, IExternalInputProvider, ICacheFingerprintProvider
    {
        public ImmutableArray<DeclaredExternalInput> GetExternalInputs(
            ExternalInputContext context,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            return [new(profilePath, ExternalInputKind.File)];
        }

        public bool TryResolve(
            NativeTargetRequest request,
            [NotNullWhen(true)] out NativeTargetDescriptor? target
        ) {
            target = null;
            if (request.targetId.value != "example-private-host") return false;
            Profile profile = JsonSerializer.Deserialize<Profile>(File.ReadAllBytes(profilePath), new JsonSerializerOptions
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectNullableAnnotations = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
            }) ?? throw new InvalidDataException("The target profile is empty.");
            if (request.tripleOverride is string triple && triple != profile.triple)
                throw new NotSupportedException("The private target accepts only its exact declared compiler triple.");
            string root = Path.GetDirectoryName(profilePath)!;
            NativeToolchainDescriptor input = request.toolchain;
            bool explicitSdk = input.sysRoot is not null || input.systemIncludeFolders.Count != 0 || input.cxxSystemIncludeFolders.Count != 0;
            NativeToolchainDescriptor sdk = new(input.compilerPath,
                explicitSdk ? input.sysRoot : profile.sysRoot is string sysroot ? Path.GetFullPath(sysroot, root) : null,
                explicitSdk ? input.systemIncludeFolders : profile.systemIncludeDirectories.Select(path => Path.GetFullPath(path, root)),
                profile.defines.Concat(input.defines), input.arguments,
                explicitSdk ? input.cxxSystemIncludeFolders : profile.cxxSystemIncludeDirectories.Select(path => Path.GetFullPath(path, root)),
                input.sdkVersion, input.compilerVersion);
            target = new(request.targetId, profile.platformId, profile.architectureId, profile.abiId, profile.triple, sdk,
                new(profile.requiresExplicitSdk, profile.supportsDynamicModules), profile.byteOrder)
            {
                sourceConventions = profile.sourceConventions,
                headerSearchPaths = profile.headerSearchPaths.Select(path => path with { path = Path.GetFullPath(path.path, root) }).ToImmutableArray()
            };
            return true;
        }

        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            ImmutableArray<DeclaredExternalInput> inputs = [new(profilePath, ExternalInputKind.File),
                .. context.target.toolchain.systemIncludeFolders.Concat(context.target.toolchain.cxxSystemIncludeFolders)
                    .Distinct(StringComparer.Ordinal).Select(path => new DeclaredExternalInput(path, ExternalInputKind.Directory))];
            return new(new(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(profilePath)))), inputs);
        }
    }

    private sealed record Profile
    {
        [JsonRequired] public string platformId { get; init; } = null!;
        [JsonRequired] public string architectureId { get; init; } = null!;
        [JsonRequired] public string abiId { get; init; } = null!;
        [JsonRequired] public string triple { get; init; } = null!;
        [JsonRequired] public NativeByteOrder byteOrder { get; init; }
        [JsonRequired] public bool requiresExplicitSdk { get; init; }
        [JsonRequired] public bool supportsDynamicModules { get; init; }
        [JsonRequired] public NativeSourceConventions sourceConventions { get; init; } = null!;
        public ImmutableArray<HeaderSearchPath> headerSearchPaths { get; init; } = [];
        public string? sysRoot { get; init; }
        public string[] systemIncludeDirectories { get; init; } = [];
        public string[] cxxSystemIncludeDirectories { get; init; } = [];
        public string[] defines { get; init; } = [];
    }
}
