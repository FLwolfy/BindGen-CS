using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Tests.NeutralPlugin;
using Xunit;

namespace BGCS.Tests;

public sealed class SourceFixtureConfigurationTests
{
    [Theory]
    [InlineData(SourceLanguage.C, 12, false)]
    [InlineData(SourceLanguage.C, 13, false)]
    [InlineData(SourceLanguage.C, 13, true)]
    [InlineData(SourceLanguage.Cpp, 12, false)]
    [InlineData(SourceLanguage.Cpp, 13, false)]
    [InlineData(SourceLanguage.Cpp, 13, true)]
    public async Task InstalledFixtureSettingsControlEveryContributionAndBothCompleteFingerprints(SourceLanguage language, int increment, bool invalidManaged)
    {
        using SourceFixturePlugin plugin = new();
        string pathBase = Path.GetFullPath(Path.GetTempPath());
        JsonElement settings = JsonSerializer.SerializeToElement(new { increment, invalidManaged });
        Registry registry = new();
        plugin.Configure(registry, new(plugin.descriptor.id, settings, pathBase));
        ISourceContributor source = Assert.IsAssignableFrom<ISourceContributor>(registry.service);
        NativeTargetDescriptor target = new(new("synthetic-x64"), "synthetic", "x64", "synthetic", "x86_64-synthetic", new());
        CacheFingerprintContext context = new(settings, pathBase, target);
        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "source-fixture-v2:" + increment.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + ":" + invalidManaged)));
        Assert.Equal(expected, plugin.CaptureFingerprint(context, CancellationToken.None).fingerprint.value);
        Assert.Equal(expected, Assert.IsAssignableFrom<ICacheFingerprintProvider>(source).CaptureFingerprint(context, CancellationToken.None).fingerprint.value);
        FrontendRequest frontend = new(target, language, []) { pathBase = pathBase };
        SourceContribution contribution = await source.ContributeAsync(new("fixture", "fixture", frontend, "Example.Source", "NativeApi", "BGCS.Runtime"), CancellationToken.None);
        Assert.Equal(EvidenceLevel.Declared, contribution.evidence.level);
        Assert.Equal(4, contribution.artifacts.Length);
        SourceContributionArtifact native = Assert.Single(contribution.artifacts, item => item.role == SourceContributionRole.NativeSource);
        Assert.Equal(language, native.language);
        Assert.EndsWith(language == SourceLanguage.Cpp ? ".cpp" : ".c", native.relativePath, StringComparison.Ordinal);
        Assert.Contains("value + " + increment.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + ";", Encoding.UTF8.GetString(native.content.AsSpan()));
        string managed = Encoding.UTF8.GetString(Assert.Single(contribution.artifacts, item => item.role == SourceContributionRole.ManagedSource).content.AsSpan());
        if (invalidManaged) Assert.Equal("#error INSTALLED_SOURCE_CONTRIBUTION_REJECTED\n", managed);
        else Assert.Contains("Marker => " + (increment + 29).ToString(global::System.Globalization.CultureInfo.InvariantCulture), managed);
        Assert.True(Assert.Single(contribution.artifacts, item => item.role == SourceContributionRole.PublicHeader).exposeToBindings);
        Assert.Equal("object", plugin.descriptor.configurationSchema.GetProperty("type").GetString());
        Assert.False(plugin.descriptor.configurationSchema.GetProperty("additionalProperties").GetBoolean());
    }

    private sealed class Registry : IExtensionRegistryBuilder
    {
        internal object? service;
        public void RegisterFactory<TService>(string id, Func<TService> factory) where TService : class
        {
            Assert.Equal("fixture.source/helpers", id);
            Assert.Null(service);
            service = factory();
        }
    }
}
