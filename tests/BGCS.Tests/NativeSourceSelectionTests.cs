using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeSourceSelectionTests
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task ExactPublicSelection_PreservesRequiredDependencyFieldsWithoutUnrelatedExports(SourceLanguage language)
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "dependency.h"), "struct Item { int value; }; int dependency_only(void);");
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "entry.h"), "#include \"dependency.h\"\nint read_item(const struct Item* input); int unselected_entry(void);");
        BindingConfiguration configuration = Configuration(language) with { source = Configuration(language).source with
            { selection = new() { includeDeclarations = ["read_item"] } } };
        await using BindingHost host = Host();
        InspectionResult result = await host.InspectAsync(new(configuration, scratch.root));
        Assert.True(result.success, string.Join(Environment.NewLine, result.diagnostics));
        TargetInspection target = Assert.Single(result.targets);
        Assert.Contains(target.native.declarations, declaration => declaration.name == "Item");
        Assert.Contains(target.native.declarations, declaration => declaration.name == "value");
        Assert.DoesNotContain(target.native.declarations, declaration => declaration.name is "dependency_only" or "unselected_entry");
        if (language == SourceLanguage.C) Assert.Equal("read_item", Assert.Single(target.interop.functions).exportName);
        else
        {
            Assert.Equal("read_item", target.native.GetDeclaration(Assert.Single(target.bridge!.exports).operation.declaration!.Value).name);
            Assert.DoesNotContain(target.bridge.exports, export => export.operation.kind == BridgeOperationKind.DefaultConstruct);
        }
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task ExplicitRequiredHeaderExclusion_ProducesAnActionableFailure(SourceLanguage language)
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "dependency.h"), "struct Item { int value; };");
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "entry.h"), "#include \"dependency.h\"\nint read_item(const struct Item* input);");
        BindingConfiguration configuration = Configuration(language) with { source = Configuration(language).source with
            { selection = new() { includeDeclarations = ["read_item"], excludeHeaders = ["dependency.h"] } } };
        await using BindingHost host = Host();
        GenerationResult result = await host.GenerateAsync(new(configuration, scratch.root));
        Assert.False(result.success);
        Assert.Contains("required native storage or cleanup", result.GetDiagnosticSummary());
        Assert.Contains("dependency.h", result.GetDiagnosticSummary());
        Assert.Contains("read_item", result.GetDiagnosticSummary());
        Assert.False(Directory.Exists(Path.Combine(scratch.root, "Generated")));
    }

    [Fact]
    public async Task ExcludedDeclaredDefaultArgumentConstructor_IsNotReplacedBySyntheticConstruction()
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "entry.h"), """
            namespace Demo { class Service { public: explicit Service(int value = 1) : m_value(value) {} int Read() const { return m_value; } private: int m_value; }; }
            """);
        BindingConfiguration configuration = Configuration(SourceLanguage.Cpp) with { source = Configuration(SourceLanguage.Cpp).source with
            { selection = new() { includeDeclarations = ["Demo::Service"], excludeDeclarations = ["Demo::Service::Service(int)"] } } };
        await using BindingHost host = Host();
        InspectionResult result = await host.InspectAsync(new(configuration, scratch.root));
        Assert.True(result.success, string.Join(Environment.NewLine, result.diagnostics));
        TargetInspection target = Assert.Single(result.targets);
        Assert.Contains(target.native.declarations, declaration => declaration.role == "Constructor");
        Assert.True(Assert.Single(target.native.declarations, declaration => declaration.name == "Service" && declaration.kind == "Class").isDefaultConstructible);
        Assert.DoesNotContain(target.bridge!.exports, export => export.operation.kind is BridgeOperationKind.Construct or BridgeOperationKind.DefaultConstruct);
        Assert.Equal("Read", target.native.GetDeclaration(Assert.Single(target.bridge.exports).operation.declaration!.Value).name);
    }

    [Fact]
    public async Task StandalonePublicClassWithoutDeclaredFunctions_RetainsItsOpaqueIdentityAndDefaultConstruction()
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "entry.h"), "class Standalone { public: int value = 42; }; class Forward;");
        await using BindingHost host = Host();
        InspectionResult result = await host.InspectAsync(new(Configuration(SourceLanguage.Cpp), scratch.root));
        Assert.True(result.success, string.Join(Environment.NewLine, result.diagnostics));
        TargetInspection target = Assert.Single(result.targets);
        Assert.Contains(target.bridge!.types, type => type.kind == BridgeTypeKind.Handle && target.native.GetType(type.nativeType!.Value).spelling == "Forward");
        BridgeExport constructor = Assert.Single(target.bridge.exports, export => export.operation.kind == BridgeOperationKind.DefaultConstruct);
        Assert.Equal("Standalone", target.native.GetType(constructor.operation.ownerType!.Value).spelling);
        Assert.Contains(target.bridge.exports, export => export.id == constructor.resultRelease);
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task SelectedOwnedFactory_RetainsItsConfiguredCleanupFromADependencyHeader(SourceLanguage language)
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "dependency.h"), "struct Item { int value; }; void release_item(struct Item* item); int unrelated(void);");
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "entry.h"), "#include \"dependency.h\"\nstruct Item* create_item(void);");
        BindingConfiguration configuration = Configuration(language) with
        {
            source = Configuration(language).source with { selection = new() { includeDeclarations = ["create_item"] } },
            semantics = new() { ownedResults = [new() { function = "create_item", releaseFunction = "release_item", releaseDomain = "selection", releaseThreadDomain = "any" }] }
        };
        await using BindingHost host = Host();
        InspectionResult result = await host.InspectAsync(new(configuration, scratch.root));
        Assert.True(result.success, string.Join(Environment.NewLine, result.diagnostics));
        TargetInspection target = Assert.Single(result.targets);
        Assert.Equal(new[] { "create_item", "release_item" }, target.interop.functions.Select(function => function.sourceQualifiedName ?? function.exportName)
            .Where(name => name is "create_item" or "release_item").Order(StringComparer.Ordinal));
        Assert.DoesNotContain(target.interop.functions, function => (function.sourceQualifiedName ?? function.exportName) == "unrelated");
        GenerationResult excluded = await host.GenerateAsync(new(configuration with { source = configuration.source with
            { selection = configuration.source.selection with { excludeDeclarations = ["release_item"] } } }, scratch.root));
        Assert.False(excluded.success);
        Assert.Contains("required native storage or cleanup", excluded.GetDiagnosticSummary());
        Assert.Contains("release_item", excluded.GetDiagnosticSummary());
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task OwnedSemantics_AcceptsTheExactStableIdentityCopiedFromInspection(SourceLanguage language)
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "entry.h"), "struct Item { int value; }; struct Item* create_item(void); void release_item(struct Item* item);");
        await using BindingHost host = Host();
        BindingConfiguration configuration = Configuration(language);
        InspectionResult initial = await host.InspectAsync(new(configuration, scratch.root));
        Assert.True(initial.success, string.Join(Environment.NewLine, initial.diagnostics));
        var declarations = Assert.Single(initial.targets).native.declarations;
        string factory = Assert.Single(declarations, declaration => declaration.name == "create_item").id.value;
        string release = Assert.Single(declarations, declaration => declaration.name == "release_item").id.value;
        configuration = configuration with
        {
            source = configuration.source with { selection = new() { includeDeclarations = [factory] } },
            semantics = new() { ownedResults = [new() { function = factory, releaseFunction = release, releaseDomain = "stable-selection", releaseThreadDomain = "any" }] }
        };
        InspectionResult selected = await host.InspectAsync(new(configuration, scratch.root));
        Assert.True(selected.success, string.Join(Environment.NewLine, selected.diagnostics));
        TargetInspection target = Assert.Single(selected.targets);
        var primary = Assert.Single(target.interop.functions, function => function.sourceDeclaration?.value == factory && function.inputAdapterOf is null);
        var cleanup = Assert.Single(target.interop.functions, function => function.sourceDeclaration?.value == release && function.inputAdapterOf is null);
        var ownership = primary.error?.valueParameter is { } result
            ? Assert.Single(primary.parameters, parameter => parameter.id == result).ownership : primary.returnOwnership;
        Assert.NotNull(ownership);
        Assert.Equal(cleanup.id, ownership.releaseFunction);
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task OwnedSemantics_AcceptsTheCanonicalGlobalQualifier(SourceLanguage language)
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "entry.h"), "struct Item { int value; }; struct Item* create_item(void); void release_item(struct Item* item);");
        BindingConfiguration configuration = Configuration(language) with
        {
            source = Configuration(language).source with { selection = new() { includeDeclarations = ["::create_item"] } },
            semantics = new() { ownedResults = [new() { function = "::create_item", releaseFunction = "::release_item", releaseDomain = "qualified-selection", releaseThreadDomain = "any" }] }
        };
        await using BindingHost host = Host();
        InspectionResult result = await host.InspectAsync(new(configuration, scratch.root));
        Assert.True(result.success, string.Join(Environment.NewLine, result.diagnostics));
        TargetInspection target = Assert.Single(result.targets);
        Assert.Contains(target.interop.functions, function => function.exportName == "release_item" || function.sourceQualifiedName == "release_item");
    }

    [Fact]
    public async Task ExplicitTemplateRequest_CannotRestoreAnExcludedSpecialization()
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "entry.h"), "namespace Demo { template<class T> class Box { public: explicit Box(T value) : m_value(value) {} T Read() const { return m_value; } private: T m_value; }; }");
        BindingConfiguration configuration = Configuration(SourceLanguage.Cpp) with
        {
            source = Configuration(SourceLanguage.Cpp).source with { selection = new() { excludeDeclarations = ["Demo::Box<int>"] } },
            cpp = new() { templateInstantiations = new() { types = [new("box", "Demo::Box<int>")] } }
        };
        await using BindingHost host = Host();
        GenerationResult result = await host.GenerateAsync(new(configuration, scratch.root));
        Assert.False(result.success);
        Assert.Contains("explicit generation request", result.GetDiagnosticSummary());
        Assert.Contains("Demo::Box<int>", result.GetDiagnosticSummary());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitShimRequest_CannotRestoreAnExcludedExportOrHeader(bool byHeader)
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "entry.h"), "int ordinary(void);");
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "shim.h"), "extern \"C\" int shim_step(int value);");
        BindingConfiguration configuration = Configuration(SourceLanguage.Cpp) with
        {
            source = Configuration(SourceLanguage.Cpp).source with { selection = new() { excludeHeaders = byHeader ? ["shim.h"] : [], excludeDeclarations = byHeader ? [] : ["shim_step"] } },
            cpp = new() { nativeShims = [new() { id = "shim", publicHeaders = ["shim.h"], exports = ["shim_step"] }] }
        };
        await using BindingHost host = Host();
        GenerationResult result = await host.GenerateAsync(new(configuration, scratch.root));
        Assert.False(result.success);
        Assert.Contains("explicit generation request", result.GetDiagnosticSummary());
        Assert.Contains("shim_step", result.GetDiagnosticSummary());
    }

    [Theory]
    [InlineData("Demo::Calculator")]
    [InlineData("Demo::Calculator::Compute(int) const")]
    public async Task RequiredVirtualInterfaceOrSlotExclusion_IsRejectedAfterReparse(string excluded)
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "entry.h"), "namespace Demo { class Calculator { public: virtual ~Calculator() noexcept = default; virtual int Compute(int value) const = 0; }; inline int Ordinary() { return 1; } }");
        BindingConfiguration configuration = Configuration(SourceLanguage.Cpp) with
        {
            source = Configuration(SourceLanguage.Cpp).source with { selection = new() { includeDeclarations = ["Demo::Ordinary"], excludeDeclarations = [excluded] } },
            cpp = new() { virtualCallbacks = [new("calculator", "Demo::Calculator")] }
        };
        await using BindingHost host = Host();
        GenerationResult result = await host.GenerateAsync(new(configuration, scratch.root));
        Assert.False(result.success);
        Assert.Contains("required native storage or cleanup", result.GetDiagnosticSummary());
        Assert.Contains("Calculator", result.GetDiagnosticSummary());
    }

    private static BindingConfiguration Configuration(SourceLanguage language) => new()
    {
        schemaVersion = 1, library = new() { id = "selection", nativeName = "selection" },
        source = new() { language = language, headers = ["entry.h"], parseMacros = false },
        api = new() { @namespace = "Generated.Selection", name = "SelectionApi" }, cache = new() { enabled = false }
    };

    private static BindingHost Host()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0")).Build();
    }

    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-native-selection-" + Guid.NewGuid().ToString("N"));
        internal Scratch() => Directory.CreateDirectory(root);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
