using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Analysis.Declarations;
using BGCS.Analysis.Interop;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Tests;

public sealed class ObjectiveCAnalysisTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicHostAnalyzeReturnsDetachedObjectiveCFactsWithoutAnAppleSdkOrCImportClaim(bool retainBodies)
    {
        string repository = Repository(), root = Path.Combine(Path.GetTempPath(), "bgcs-host-objc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "facts.h");
            File.Copy(Path.Combine(repository, "tests", "fixtures", "native", "ObjectiveCFacts.h"), header);
            string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(Path.Combine(repository, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0")).Build();
            AnalysisResult result = await host.AnalyzeAsync(new([header], SourceLanguage.ObjC, root)
            { targetId = "macos-arm64-darwin", arguments = ["-fblocks", "-fobjc-runtime=macosx-10.12", "-fobjc-weak"], parseMacros = false, retainFunctionBodies = retainBodies });
            Assert.True(result.success, result.GetDiagnosticSummary());
            NativeModule module = result.module!;
            Assert.Equal(SourceLanguage.ObjC, module.language);
            Assert.Equal("arm64-apple-darwin", module.triple);
            Assert.Contains(result.snapshot!.files, input => input.path == header);
            NativeDeclaration bag = Assert.Single(module.declarations, declaration => declaration.name == "Bag" && declaration.objectiveCContainer?.kind == NativeObjectiveCContainerKind.Interface);
            Assert.Equal("Root", module.GetDeclaration(bag.objectiveCContainer!.baseInterface!.Value).name);
            Assert.Equal("CombinedProtocol", module.GetDeclaration(Assert.Single(bag.objectiveCContainer.protocols)).name);
            NativeDeclaration property = Assert.Single(module.declarations, declaration => declaration.parent == bag.id && declaration.name == "name" && declaration.objectiveCProperty is not null);
            Assert.True(property.objectiveCProperty!.attributes!.Value.HasFlag(NativeObjectiveCPropertyAttributes.Copy));
            Assert.Equal("displayName", module.GetDeclaration(property.objectiveCProperty.getter!.Value).objectiveCMethod!.selector);
            DeclarationSelection selected = new DeclarationSelector().Select(module, new() { includeDeclarations = [bag.id.value] }, [header], root);
            Assert.Empty(new NativeModuleVerifier().Validate(selected.module));
            Assert.Contains(selected.module.declarations, declaration => declaration.id == property.id);
            Assert.Contains(selected.module.declarations, declaration => declaration.objectiveCContainer?.kind == NativeObjectiveCContainerKind.Protocol);
            Assert.Equal(2,bag.objectiveCContainer.genericParameters.Length);
            Assert.All(bag.objectiveCContainer.genericParameters,parameter=>Assert.Contains(selected.module.types,type=>type.id==parameter));
            Assert.Contains(new CDeclarationAnalyzer().Analyze(module, "objc").diagnostics, diagnostic => diagnostic.isFailure);
            Assert.False(Directory.Exists(Path.Combine(root, "Generated")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ObjectiveCFactsDoNotEnableUnimplementedMessageDispatchGeneration()
    {
        BindingConfiguration configuration = new()
        {
            schemaVersion = 1, library = new() { id = "objc-facts", nativeName = "objc-facts" },
            source = new() { language = SourceLanguage.ObjC, headers = ["facts.h"] }
        };
        ArgumentException failure = Assert.ThrowsAny<ArgumentException>(() => new GenerationRequest(configuration, Path.GetTempPath()));
        Assert.Contains("language", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string Repository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
