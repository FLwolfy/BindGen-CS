using System;
using System.Linq;
using System.Reflection;
using BGCS.Core.Native;
using BGCS.NativeBridge.Planning;
using BGCS.CSharp;
using Xunit;

namespace BGCS.Architecture.Tests;

public sealed class RetiredApiBoundaryTests
{
    [Fact]
    public void HostLoweringAndBackendReferenceNeutralContractsWithoutTheParserImplementation()
    {
        Assembly[] assemblies = [typeof(BindingHost).Assembly, typeof(NativeBridgePlanner).Assembly, typeof(CSharpBackend).Assembly];
        foreach (Assembly assembly in assemblies)
        {
            string?[] names = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
            Assert.Contains("BGCS.Core", names);
            Assert.DoesNotContain(names, name => name is not null && (name.StartsWith("ClangSharp", StringComparison.Ordinal)
                || name.StartsWith("BGCS.Frontend", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void OldFacadeFlatConfigurationAndMutableBindingModelAreAbsent()
    {
        Assert.Null(typeof(BindingHost).Assembly.GetType("BGCS.Facade.CsCodeGenerator"));
        Assert.Null(typeof(BindingHost).Assembly.GetType("BGCS.Configuration.CsCodeGeneratorConfig"));
        Assert.Null(typeof(NativeModule).Assembly.GetType("BGCS.Core.BindingModule"));
        Assert.Null(typeof(CSharpBackend).Assembly.GetType("BGCS.CSharp.Emission.CSharpEmitter"));
        Assert.DoesNotContain(typeof(BindingHost).Assembly.GetExportedTypes(), type => type.Namespace?.StartsWith("BGCS.Facade", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void BackendDoesNotPublishTheRetiredStandaloneAuthoringParserGraph()
    {
        string[] retiredNamespaces = ["BGCS.CSharp.CSharp", "BGCS.CSharp.Cpp", "BGCS.CSharp.Lexing", "BGCS.CSharp.Parsing", "BGCS.CSharp.Syntax", "BGCS.CSharp.Diagnostics"];
        Type[] backendTypes = typeof(CSharpBackend).Assembly.GetTypes();
        Assert.DoesNotContain(backendTypes, type => retiredNamespaces.Any(prefix => type.Namespace == prefix || type.Namespace?.StartsWith(prefix + ".", StringComparison.Ordinal) == true));
        Assert.Contains(backendTypes, type => type.FullName == "BGCS.CSharp.Processing.CSharpPreprocessor" && type.IsPublic);
        Assert.Contains(backendTypes, type => type.FullName == "BGCS.CSharp.Processing.SyntaxArtifactProcessor" && type.IsPublic);
    }

    [Fact]
    public void GeneratorSurfaceExcludesConsumerBuildAndPackagingCapabilities()
    {
        Assert.DoesNotContain(typeof(BindingHost).GetMethods(), method => method.Name is "BuildAsync" or "BuildWorkspaceAsync" or "NativeBuildAsync");
        string[] retiredTypes =
        [
            "BGCS.BuildRequest", "BGCS.BuildResult", "BGCS.ManifestNativeBuildRequest",
            "BGCS.Configuration.NativeBuildConfiguration", "BGCS.Configuration.ManagedBuildConfiguration"
        ];
        foreach (string type in retiredTypes) Assert.Null(typeof(BindingHost).Assembly.GetType(type));
        Assert.DoesNotContain(typeof(NativeModule).Assembly.GetExportedTypes(), type =>
            type.Name is "INativeBuildProvider" or "INativeArtifactPackager" or "NativeBuildPlan" or "NativeBuildManifest");
        foreach (Assembly assembly in new[] { typeof(BindingHost).Assembly, typeof(NativeBridgePlanner).Assembly, typeof(CSharpBackend).Assembly,
            typeof(BGCS.Infrastructure.Publishing.ArtifactPublisher).Assembly, typeof(BGCS.Runtime.Bool8).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name is "BGCS.NativeBuild" or "BGCS.RepositoryTools" or "BGCS.MSBuild"
                || reference.Name?.StartsWith("Microsoft.Build", StringComparison.Ordinal) == true);
        Assert.Null(typeof(BGCS.Configuration.TargetConfiguration).GetProperty("toolchain"));
    }

    [Fact]
    public void NeutralEngineAndAlgorithmsDoNotReferenceConcretePlatformImplementations()
    {
        foreach (Assembly assembly in new[] { typeof(BindingHost).Assembly, typeof(NativeBridgePlanner).Assembly,
            typeof(CSharpBackend).Assembly, typeof(BGCS.Analysis.Interop.NativeModuleVerifier).Assembly,
            typeof(BGCS.Infrastructure.Publishing.ArtifactPublisher).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name is not null
                && (reference.Name.StartsWith("BGCS.Extension.", StringComparison.Ordinal)
                    || reference.Name.StartsWith("BGCS.Hosting.", StringComparison.Ordinal)
                    || reference.Name.StartsWith("BGCS.Targets.", StringComparison.Ordinal)
                    || reference.Name.StartsWith("BGCS.Interop.", StringComparison.Ordinal)
                    || reference.Name.StartsWith("BGCS.Frontend.", StringComparison.Ordinal)));
    }

    [Fact]
    public void CompleteEntryPointOwnsStandardCompositionWithoutDependingOnOptionalExtensionProducts()
    {
        string?[] references = typeof(BindingGenerator).Assembly.GetReferencedAssemblies().Select(value => value.Name).ToArray();
        Assert.Contains("BGCS.Engine", references);
        Assert.Contains("BGCS.Hosting.Standard", references);
        Assert.Contains("BGCS.Frontend.Clang", references);
        Assert.DoesNotContain(references, name => name?.StartsWith("BGCS.Extension.", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(typeof(BindingHost).GetMethods(), method => method.Name == "CreateDefault");
        Assert.Contains(typeof(BindingGenerator).GetMethods(), method => method.Name == "CreateDefault");
    }
}
