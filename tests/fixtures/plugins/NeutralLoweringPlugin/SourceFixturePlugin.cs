using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;

namespace BGCS.Tests.NeutralPlugin;

/// <summary>Supplies a complete generated-input fixture while referencing Contracts alone.</summary>
public sealed class SourceFixturePlugin : IBindingPlugin, ICacheFingerprintProvider
{
    private int m_increment = 12;
    private bool m_invalidManaged;

    /// <inheritdoc/>
    public PluginDescriptor descriptor => new("fixture.source-plugin", "1.0.0", [new("fixture.source/helpers", ExtensionPort.SourceContributor)])
    {
        configurationSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                increment = new { type = "integer", minimum = -1000, maximum = 1000 },
                invalidManaged = new { type = "boolean" }
            }
        })
    };

    /// <inheritdoc/>
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        m_increment = configuration.settings.TryGetProperty("increment", out JsonElement increment) ? increment.GetInt32() : 12;
        m_invalidManaged = configuration.settings.TryGetProperty("invalidManaged", out JsonElement invalid) && invalid.GetBoolean();
        registry.RegisterFactory<ISourceContributor>("fixture.source/helpers", _ => new Sources(m_increment, m_invalidManaged));
    }

    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => Fingerprint(m_increment, m_invalidManaged);

    /// <inheritdoc/>
    public void Dispose() { }

    private static CacheFingerprintContribution Fingerprint(
        int increment,
        bool invalidManaged
    )
        => new(new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "source-fixture-v2:" + increment.ToString(CultureInfo.InvariantCulture) + ":" + invalidManaged)))), []);

    private sealed class Sources(
        int increment,
        bool invalidManaged
    ) : ISourceContributor, ICacheFingerprintProvider
    {
        public string id => "fixture.source/helpers";
        public int priority => 0;

        public ValueTask<SourceContribution> ContributeAsync(
            SourceContributionRequest request,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.source.snapshotExcludedDirectories.IsEmpty)
                throw new InvalidOperationException("The source contribution request exposed private output directories.");
            bool cpp = request.source.language == SourceLanguage.Cpp;
            const string export = "#if defined(_WIN32)\n#define FIXTURE_EXPORT __declspec(dllexport)\n#else\n#define FIXTURE_EXPORT __attribute__((visibility(\"default\")))\n#endif\n";
            string declaration = cpp ? "extern \"C\" FIXTURE_EXPORT int contributed_add(int value);\n" : "FIXTURE_EXPORT int contributed_add(int value);\n";
            return ValueTask.FromResult(new SourceContribution([
                SourceContributionArtifact.FromUtf8("generated.h", SourceContributionRole.PublicHeader,
                    cpp ? "text/x-c++-header" : "text/x-c-header", "#pragma once\n" + export + declaration,
                    request.source.language, exposeToBindings: true),
                SourceContributionArtifact.FromUtf8(cpp ? "generated.cpp" : "generated.c", SourceContributionRole.NativeSource,
                    cpp ? "text/x-c++" : "text/x-c", "#include \"contributions/fixture.source/helpers/generated.h\"\nint contributed_add(int value) { return value + " + increment.ToString(CultureInfo.InvariantCulture) + "; }\n", request.source.language),
                SourceContributionArtifact.FromUtf8("Helper.cs", SourceContributionRole.ManagedSource, "text/x-csharp",
                    invalidManaged ? "#error INSTALLED_SOURCE_CONTRIBUTION_REJECTED\n" :
                    "namespace " + request.managedNamespace + "; public static class ContributedHelper { public static int Marker => " + (increment + 29).ToString(CultureInfo.InvariantCulture) + "; }\n"),
                SourceContributionArtifact.FromUtf8("payload.txt", SourceContributionRole.Resource, "text/plain", "fixture resource\n")
            ], new(EvidenceLevel.Declared, descriptorOrigin)));
        }

        private const string descriptorOrigin = "Contracts-only source fixture declares its generated inputs.";

        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        )
            => Fingerprint(increment, invalidManaged);
    }
}
