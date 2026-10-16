using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.Tests;

public sealed class ExactIrRetirementSourceTests
{
    [Fact]
    public async Task RejectedCloneReturnCannotBeReintroducedByTheSameFunctionsMemberPresentation()
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"),
                "typedef struct NativeThing { int value; } NativeThing;\nNativeThing* NativeThing_Clone(NativeThing* self);\n");
            BindingConfiguration configuration = Configuration() with
            {
                source = Configuration().source with { parseMacros = false },
                api = Configuration().api with
                {
                    names = ImmutableDictionary<string, string>.Empty.Add("NativeThing_Clone", "Clone"),
                    memberFunctions = [new("NativeThing_Clone", "NativeThing", "self") { objectName = "NativeThingObject" }]
                }
            };
            await using BindingHost host = Host();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.generation.targetModels);
            var clone = Assert.Single(target.interop.functions);
            Assert.Equal("NativeThing_Clone", clone.exportName);
            Assert.Null(clone.returnOwnership);
            Assert.Null(clone.returnString);
            Assert.Contains(result.generation.diagnostics, diagnostic => diagnostic.code == BindingDiagnosticCodes.C_OWNERSHIP
                && diagnostic.severity == BindingDiagnosticSeverity.Warning && diagnostic.location?.file == "native.h");
            Assert.NotNull(Assert.Single(target.managed!.methods).receiver);
            CompilationUnitSyntax[] units = ManagedFiles(result).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot()).ToArray();
            MethodDeclarationSyntax declaration = Assert.Single(units.SelectMany(unit => unit.DescendantNodes().OfType<MethodDeclarationSyntax>()),
                method => method.Identifier.ValueText == "Clone");
            Assert.Equal("RawApi", Assert.IsType<ClassDeclarationSyntax>(declaration.Parent).Identifier.ValueText);
            Assert.Equal("NativeThing*", declaration.ReturnType.ToString());
            Assert.Equal("NativeThing*", Assert.Single(declaration.ParameterList.Parameters).Type!.ToString());
            Assert.DoesNotContain(units.SelectMany(unit => unit.DescendantNodes().OfType<TypeDeclarationSyntax>()),
                type => type.Identifier.ValueText is "NativeThingObject" or "NativeThingPtr");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task NamedMissingStringCleanupRejectsInvalidConfigurationAndPreservesTheCompletePublication()
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "char* bgcs_create_name(void);\nvoid bgcs_free_name(char* value);\n");
            BindingConfiguration configuration = Configuration() with
            {
                source = Configuration().source with { parseMacros = false },
                semantics = new()
                {
                    ownedResults = [new() { function = "bgcs_create_name", releaseFunction = "bgcs_free_name", releaseDomain = "native-name", releaseThreadDomain = "any" }],
                    stringResults = [new() { function = "bgcs_create_name", encoding = NativeTextEncoding.Utf8, zeroTerminated = true, allowsNull = true }]
                }
            };
            await using BindingHost host = Host();
            BuildResult initial = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(initial.success, initial.GetDiagnosticSummary());
            Assert.Contains(ManagedFiles(initial).Select(File.ReadAllText), source => source.Contains("Utf8Codec.Decode(", StringComparison.Ordinal));
            Dictionary<string, string> previous = Snapshot(initial.outputRoot);
            const string missingCallable = "bgcs_missing_free_name";
            BindingConfiguration invalid = configuration with
            {
                semantics = configuration.semantics with
                { ownedResults = [configuration.semantics.ownedResults[0] with { releaseFunction = missingCallable }] }
            };
            Assert.Equal(missingCallable, Assert.Single(invalid.semantics.ownedResults).releaseFunction);
            GenerationResult rejected = await host.GenerateAsync(new(invalid, root));
            Assert.False(rejected.success);
            Assert.False(rejected.published);
            Assert.Contains(missingCallable, rejected.GetDiagnosticSummary(), StringComparison.Ordinal);
            Assert.Contains(rejected.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Equal(previous.OrderBy(item => item.Key), Snapshot(initial.outputRoot).OrderBy(item => item.Key));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task MissingCallbackLifetimeAndThreadingWarnIndependentlyAndStrictPolicyPreservesRawPublication()
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                typedef void (*bgcs_callback)(int value);
                typedef bgcs_callback CallbackAlias;
                void bgcs_register(CallbackAlias const callback);
                void bgcs_unregister(void);
                void bgcs_complete(void);
                """);
            BindingConfiguration configuration = Configuration() with { source = Configuration().source with { parseMacros = false } };
            await using BindingHost host = Host();
            BuildResult initial = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(initial.success, initial.GetDiagnosticSummary());
            string[] codes = [BindingDiagnosticCodes.C_CALLBACKLIFETIME, BindingDiagnosticCodes.C_CALLBACKTHREADING];
            foreach (string code in codes)
            {
                BindingDiagnostic warning = Assert.Single(initial.generation.diagnostics, diagnostic => diagnostic.code == code);
                Assert.Equal(BindingDiagnosticSeverity.Warning, warning.severity);
                Assert.Equal("native.h", warning.location?.file);
                Assert.Contains("bgcs_register:callback", warning.message, StringComparison.Ordinal);
            }
            var register = Assert.Single(Assert.Single(initial.generation.targetModels).interop.functions, function => function.exportName == "bgcs_register");
            Assert.Null(Assert.Single(register.parameters).callback);
            var declarations = ManagedFiles(initial).SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot()
                .DescendantNodes().OfType<MethodDeclarationSyntax>()).ToArray();
            Assert.Contains(declarations, method => method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText == "RawApi"
                && method.ParameterList.Parameters.Any(parameter => parameter.Type is FunctionPointerTypeSyntax));
            Dictionary<string, string> previous = Snapshot(initial.outputRoot);
            BindingConfiguration strict = configuration with { execution = new() { diagnostics = new() { warningsAsErrors = true } } };
            GenerationResult rejected = await host.GenerateAsync(new(strict, root));
            Assert.False(rejected.success);
            Assert.False(rejected.published);
            foreach (string code in codes)
                Assert.Equal(BindingDiagnosticSeverity.Error, Assert.Single(rejected.diagnostics, diagnostic => diagnostic.code == code).severity);
            Assert.Equal(previous.OrderBy(item => item.Key), Snapshot(initial.outputRoot).OrderBy(item => item.Key));
            BindingConfiguration declared = strict with { semantics = new() { callbackParameters = [new()
            {
                function = "bgcs_register", parameter = "callback", lifetime = NativeLifetimeKind.Call, retained = false,
                threading = NativeCallbackThreading.CallerThread
            }] } };
            BuildResult recovered = await host.BuildAsync(new(new(declared, root)));
            Assert.True(recovered.success, recovered.GetDiagnosticSummary());
            Assert.DoesNotContain(recovered.generation.diagnostics, diagnostic => codes.Contains(diagnostic.code));
            CallbackContract facts = Assert.IsType<CallbackContract>(Assert.Single(Assert.Single(recovered.generation.targetModels).interop.functions,
                function => function.exportName == "bgcs_register").parameters[0].callback);
            Assert.Equal(NativeLifetimeKind.Call, facts.lifetime.kind);
            Assert.Equal(NativeCallbackThreading.CallerThread, facts.threading);
            Assert.Equal(false, facts.retained);
            Assert.Equal(BGCS.Contracts.Common.EvidenceLevel.UserAsserted, facts.evidence.level);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ConfiguredSingleFileContainsTheExactConstantRecordAndOriginalImportAsCompiledSemantics()
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"),
                "#define BGCS_LIMIT 42\ntypedef struct BgcsPoint { int x; int y; } BgcsPoint;\nint bgcs_add(int left,int right);\n");
            BindingConfiguration configuration = Configuration() with
            {
                api = Configuration().api with { names = ImmutableDictionary<string, string>.Empty.Add("BGCS_LIMIT", "BGCS_LIMIT") },
                output = new() { layout = "singleFile", singleFileName = "GeneratedBindings.cs" }
            };
            await using BindingHost host = Host();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string source = Assert.Single(ManagedFiles(result));
            Assert.Equal("GeneratedBindings.cs", Path.GetFileName(source));
            var constant = Assert.Single(Assert.Single(result.generation.targetModels).interop.constants, value => value.name == "BGCS_LIMIT");
            Assert.Equal(NativeScalarConstantKind.Integer, constant.value.kind);
            Assert.Equal("42", constant.value.value);
            using MemoryStream image = Compile(File.ReadAllText(source));
            AssemblyLoadContext load = new("exact-ir-single-file-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                Assembly assembly = load.LoadFromStream(image);
                FieldInfo field = Assert.Single(assembly.GetTypes().SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)),
                    field => field.Name == "BGCS_LIMIT");
                Assert.True(field.IsLiteral);
                Assert.Equal(42, Assert.IsType<int>(field.GetRawConstantValue()));
                Type point = assembly.GetType("Generated.ExactIr.BgcsPoint", throwOnError: true)!;
                Assert.Equal(8, Marshal.SizeOf(point));
                Assert.Equal(0, Marshal.OffsetOf(point, "x").ToInt32());
                Assert.Equal(4, Marshal.OffsetOf(point, "y").ToInt32());
                DllImportAttribute import = Assert.Single(assembly.GetTypes().SelectMany(type => type.GetMethods(
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    .Select(method => method.GetCustomAttribute<DllImportAttribute>()), attribute => attribute is not null)!;
                Assert.Equal("bgcs_add", import.EntryPoint);
                Assert.Equal(configuration.library.nativeName, import.Value);
            }
            finally { load.Unload(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 1, library = new() { id = "exact-ir", nativeName = "exact_ir_fixture" },
        source = new() { language = SourceLanguage.C, headers = ["native.h"], includeTransitivelyReferencedHeaders = false },
        api = new() { @namespace = "Generated.ExactIr", name = "ExactApi", importMode = BindingImportMode.DllImport },
        nativeBuild = new() { mode = "none" }, cache = new() { enabled = false }
    };

    private static string[] ManagedFiles(BuildResult result) => Directory.GetFiles(result.outputRoot, "*.cs", SearchOption.AllDirectories);
    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-exact-ir-retirement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);
    private static MemoryStream Compile(string source)
    {
        string paths = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Framework metadata is unavailable.");
        var references = paths.Split(Path.PathSeparator).Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("Exact.IR.Consumer." + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14))], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        MemoryStream image = new();
        var result = compilation.Emit(image);
        if (!result.Success) { image.Dispose(); Assert.Fail(string.Join(Environment.NewLine, result.Diagnostics)); }
        image.Position = 0;
        return image;
    }
    private static BindingHost Host()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(Path.Combine(root!.FullName,
            "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0")).Build();
    }
}
