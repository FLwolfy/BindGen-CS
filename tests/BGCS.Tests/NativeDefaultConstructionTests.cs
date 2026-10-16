using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeDefaultConstructionTests
{
    [Theory]
    [InlineData("package")]
    [InlineData("source")]
    public async Task ImplicitDefaultConstruction_UsesCompilerFactsAndExactTypedRelease(string runtimeMode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-default-construct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "default.hpp"), Header);
            BindingConfiguration configuration = Configuration(runtimeMode);
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            CppBridgeModule plan = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(Path.Combine(target, "native", "metadata", "bridge.module.json")))!;
            BridgeExport construction = Assert.Single(plan.exports, export => export.operation.kind == BridgeOperationKind.DefaultConstruct);
            Assert.Null(construction.operation.declaration);
            Assert.NotNull(construction.resultRelease);
            Assert.Empty(construction.parameters);
            var owner = plan.source.GetDeclaration(plan.source.GetType(construction.operation.ownerType!.Value).declaration!.Value);
            Assert.Equal("Demo::DefaultBox", owner.qualifiedName);
            Assert.True(owner.isDefaultConstructible);
            Assert.DoesNotContain(owner.children.Select(plan.source.GetDeclaration), declaration => declaration.role == "Constructor");
            Assert.False(plan.source.declarations.Single(declaration => declaration.kind == "Class" && declaration.qualifiedName == "Demo::Disabled").isDefaultConstructible);
            Assert.Null(plan.source.declarations.Single(declaration => declaration.kind == "Class" && declaration.qualifiedName == "Demo::Incomplete").isDefaultConstructible);
            string bridge = await File.ReadAllTextAsync(Path.Combine(target, "native", "src", "bgcs_default.cpp"));
            Assert.Contains("std::is_default_constructible<::Demo::DefaultBox>::value", bridge);
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(target, "native", "bin")));
            Invoke(Path.Combine(target, "managed"), binary, runtimeMode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DeletedImplicitConstructorUnderFinalNativeFlags_RejectsBuildAndPreservesPublication()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-default-flags-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "default.hpp"), Header);
            BindingConfiguration configuration = Configuration("package");
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult initial = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(initial.success, initial.GetDiagnosticSummary());
            string[] paths = Directory.GetFiles(initial.outputRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
            var previous = paths.ToDictionary(path => Path.GetRelativePath(initial.outputRoot, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);
            BindingConfiguration changed = configuration with { nativeBuild = configuration.nativeBuild with { compilerArguments = ["-DBGCS_BLOCK_DEFAULT=1"] } };
            BuildResult rejected = await host.BuildAsync(new(new(changed, root)));
            Assert.False(rejected.success);
            Assert.Contains("default", rejected.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(previous.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(initial.outputRoot, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(initial.outputRoot, path)).Order(StringComparer.Ordinal));
            foreach (var file in previous) Assert.Equal(file.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(initial.outputRoot, file.Key)))));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BindingConfiguration Configuration(string runtimeMode) => new()
    {
        schemaVersion = 1, library = new() { id = "default", nativeName = "default_fixture" },
        source = new() { language = SourceLanguage.Cpp, headers = ["default.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
        api = new() { @namespace = "Generated.DefaultConstruction", name = "DefaultApi" },
        semantics = new() { ownedResults = [new() { function = "Demo::DefaultBox::DefaultBox()", releaseThreadDomain = "any" }] },
        runtime = new() { mode = runtimeMode }, nativeBuild = new() { mode = "sources" }, cache = new() { enabled = false }
    };

    private static void Invoke(string sources, string binary, string runtimeMode)
    {
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        string consumer = """
            using Generated.DefaultConstruction;
            public static class DefaultConsumer {
                public static int Run(string binary) {
                    using var api = DefaultApi.Load(binary);
                    using var value = api.CreateDefaultBox();
                    if (value.Read() != 42) return 1;
                    value.Dispose();
                    return value.releaseFailure is null ? 42 : 2;
                }
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create("DefaultConstruction_" + Guid.NewGuid().ToString("N"),
            Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new(LanguageVersion.CSharp14), path))
                .Append(CSharpSyntaxTree.ParseText(consumer, new(LanguageVersion.CSharp14))),
            references.Concat(runtimeMode == "package" ? [typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location] : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("BGCS.DefaultConstruction.Test", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            Assert.Equal(42, assembly.GetType("DefaultConsumer", true)!.GetMethod("Run")!.Invoke(null, [binary]));
        }
        finally { load.Unload(); }
    }

    private const string Header = """
        namespace Demo {
        struct Component {
        #ifdef BGCS_BLOCK_DEFAULT
            Component() = delete;
        #endif
            int value = 42;
        };
        class DefaultBox {
            Component m_component;
        public:
            virtual ~DefaultBox() noexcept = default;
            int Read() const { return m_component.value; }
        };
        struct Disabled { int& reference; virtual ~Disabled() noexcept = default; };
        struct Incomplete;
        inline int InspectDisabled(const Disabled* value) { return value == nullptr ? 0 : 1; }
        inline int InspectIncomplete(const Incomplete* value) { return value == nullptr ? 0 : 1; }
        }
        """;

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
