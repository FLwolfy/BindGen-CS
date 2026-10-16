using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class SourceContributionContractTests
{
    [Fact]
    public void Utf8Contribution_PreservesSupplementaryCharactersAndEmbeddedZeroWithoutBom()
    {
        const string text = "prefix\0\U0001F642\u4E2D\r\nsuffix";
        SourceContributionArtifact artifact = SourceContributionArtifact.FromUtf8("include/generated.hpp", SourceContributionRole.PublicHeader,
            "text/x-c++hdr", text, SourceLanguage.Cpp, exposeToBindings: true);

        Assert.Equal(new UTF8Encoding(false, true).GetBytes(text), artifact.content.ToArray());
        Assert.Equal(text, new UTF8Encoding(false, true).GetString(artifact.content.AsSpan()));
        Assert.Equal(SourceLanguage.Cpp, artifact.language);
        Assert.True(artifact.exposeToBindings);
        Assert.False(artifact.content.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public void Utf8Contribution_RejectsInvalidSurrogates()
    {
        foreach (string text in new[] { "\uD800", "\uDC00", "\uD800A" })
            Assert.Throws<EncoderFallbackException>(() => SourceContributionArtifact.FromUtf8("invalid.txt", SourceContributionRole.Resource, "text/plain", text));
    }

    [Fact]
    public void Utf8Contribution_RejectsNullText()
    {
        Assert.Throws<ArgumentNullException>(() => SourceContributionArtifact.FromUtf8("invalid.txt", SourceContributionRole.Resource, "text/plain", null!));
    }

    [Theory]
    [InlineData(EvidenceLevel.Declared)]
    [InlineData(EvidenceLevel.UserAsserted)]
    public void CompleteContribution_RoundTripsBinaryAndMixedSourceRolesWithoutUpgradingEvidence(EvidenceLevel level)
    {
        SourceContribution contribution = new([
            SourceContributionArtifact.FromUtf8("generated.h", SourceContributionRole.PublicHeader, "text/x-chdr", "int generated(void);", SourceLanguage.C, true),
            SourceContributionArtifact.FromUtf8("generated.c", SourceContributionRole.NativeSource, "text/x-csrc", "int generated(void) { return 42; }", SourceLanguage.C),
            SourceContributionArtifact.FromUtf8("Generated.cs", SourceContributionRole.ManagedSource, "text/x-csharp", "public readonly struct Extra { }"),
            new("lookup.bin", SourceContributionRole.Resource, "application/octet-stream", [0, 255, 254, 1])
        ], new(level, "contracts-only-fixture"));

        SourceContribution restored = JsonSerializer.Deserialize<SourceContribution>(JsonSerializer.Serialize(contribution))!;

        Assert.Equal(contribution.evidence, restored.evidence);
        Assert.NotEqual(EvidenceLevel.Verified, restored.evidence.level);
        Assert.Equal(contribution.artifacts.Length, restored.artifacts.Length);
        for (int index = 0; index < contribution.artifacts.Length; index++)
        {
            SourceContributionArtifact expected = contribution.artifacts[index];
            SourceContributionArtifact actual = restored.artifacts[index];
            Assert.Equal(expected.relativePath, actual.relativePath);
            Assert.Equal(expected.role, actual.role);
            Assert.Equal(expected.mediaType, actual.mediaType);
            Assert.Equal(expected.language, actual.language);
            Assert.Equal(expected.exposeToBindings, actual.exposeToBindings);
            Assert.Equal(expected.content.ToArray(), actual.content.ToArray());
        }
    }

    [Fact]
    public async Task ContractsOnlyPlugin_RegistersDeferredSourceFactoryAndReceivesDetachedInputs()
    {
        using IBindingPlugin plugin = new ContributionPlugin();
        Registry registry = new();
        plugin.Configure(registry, new(plugin.descriptor.id, JsonSerializer.SerializeToElement(new { }), Path.GetFullPath(Path.GetTempPath())));

        Assert.Equal(ExtensionPort.SourceContributor, Assert.Single(plugin.descriptor.services).port);
        Assert.Equal(typeof(ISourceContributor), registry.serviceType);
        Assert.Equal("fixture.sources", registry.id);
        Assert.False(registry.wasCreated);
        ISourceContributor service = Assert.IsType<Contributor>(registry.Create());
        Assert.True(registry.wasCreated);

        SourceContributionRequest request = Request();
        SourceContribution result = await service.ContributeAsync(request, CancellationToken.None);

        Assert.Equal("fixture", request.libraryId);
        Assert.Equal("fixture_native", request.nativeLibraryName);
        Assert.Equal("Fixture.Api", request.managedNamespace);
        Assert.Equal("FixtureApi", request.apiName);
        Assert.Equal("Fixture.Api.Runtime", request.runtimeNamespace);
        Assert.Empty(request.source.snapshotExcludedDirectories);
        Assert.Equal(SourceLanguage.Cpp, request.source.language);
        Assert.Equal(new[] { "-fno-rtti" }, request.source.arguments);
        Assert.Equal(new Evidence(EvidenceLevel.Declared, "fixture.sources"), result.evidence);
        Assert.Equal("fixture\0\U0001F642", Encoding.UTF8.GetString(Assert.Single(result.artifacts).content.AsSpan()));
    }

    [Fact]
    public void PublicContributionSurface_IsOwnedByDependencyNeutralContractsAssembly()
    {
        Assembly contracts = typeof(ISourceContributor).Assembly;
        Assert.All(new[] { typeof(SourceContributionRequest), typeof(SourceContribution), typeof(SourceContributionArtifact), typeof(SourceContributionRole) },
            type => Assert.Same(contracts, type.Assembly));
        Assert.DoesNotContain(contracts.GetReferencedAssemblies(), reference => reference.Name is not null &&
            (reference.Name.StartsWith("BGCS.", StringComparison.Ordinal) || reference.Name == "BGCS" ||
             reference.Name.StartsWith("Clang", StringComparison.Ordinal) || reference.Name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)));
        Assert.DoesNotContain(typeof(SourceContributionRequest).GetProperties(), property =>
            property.PropertyType == typeof(Delegate) || typeof(Delegate).IsAssignableFrom(property.PropertyType) ||
            typeof(Stream).IsAssignableFrom(property.PropertyType));
    }

    private static SourceContributionRequest Request()
    {
        string pathBase = Path.GetFullPath(Path.GetTempPath());
        NativeTargetDescriptor target = new(new("synthetic-x64"), "synthetic", "x64", "synthetic", "x86_64-synthetic", new());
        FrontendRequest source = new(target, SourceLanguage.Cpp, [Path.Combine(pathBase, "fixture.hpp")])
        {
            pathBase = pathBase,
            arguments = ["-fno-rtti"],
            includeDirectories = [pathBase],
            defines = ["FIXTURE=1"],
            snapshotExcludedDirectories = []
        };
        return new("fixture", "fixture_native", source, "Fixture.Api", "FixtureApi", "Fixture.Api.Runtime");
    }

    private sealed class ContributionPlugin : IBindingPlugin
    {
        public PluginDescriptor descriptor { get; } = new("fixture.source-plugin", "3.0.0", [new("fixture.sources", ExtensionPort.SourceContributor, 12)]);
        public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration) =>
            registry.RegisterFactory<ISourceContributor>("fixture.sources", () => new Contributor());
    }

    private sealed class Contributor : ISourceContributor
    {
        public string id => "fixture.sources";
        public int priority => 12;
        public ValueTask<SourceContribution> ContributeAsync(SourceContributionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new SourceContribution([
                SourceContributionArtifact.FromUtf8("lookup.txt", SourceContributionRole.Resource, "text/plain", "fixture\0\U0001F642")
            ], new(EvidenceLevel.Declared, id)));
        }
    }

    private sealed class Registry : IExtensionRegistryBuilder
    {
        private Func<object>? m_factory;
        public string? id { get; private set; }
        public Type? serviceType { get; private set; }
        public bool wasCreated { get; private set; }

        public void RegisterFactory<TService>(string id, Func<TService> factory) where TService : class
        {
            Assert.Null(m_factory);
            this.id = id;
            serviceType = typeof(TService);
            m_factory = () => factory();
        }

        public object Create()
        {
            wasCreated = true;
            return (m_factory ?? throw new InvalidOperationException("No contribution factory was registered."))();
        }
    }
}
