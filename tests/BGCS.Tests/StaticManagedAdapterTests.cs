using BGCS.Core.Plugins;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class StaticManagedAdapterTests
{
    [Theory]
    [InlineData("static_fixture_native")]
    [InlineData("__Internal")]
    public async Task GeneratedFromStaticValidatesProtocolAndInvokesRealNativeFunctionThroughItsAddressTable(string nativeName)
    {
        // A desktop DLL provides controllable static-adapter imports. This proves the
        // generated managed/native protocol without claiming browser or NativeAOT execution.
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs static managed adapter " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        nint module = 0;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.h"), "int twice(int value);\n");
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.c"),
                "#if defined(_WIN32)\n#define EXPORT __declspec(dllexport)\n#else\n#define EXPORT __attribute__((visibility(\"default\")))\n#endif\nEXPORT int twice(int value) { return value * 2; }\n");
            NativeTargetDescriptor resolved = NativeConsumerHarness.ParserTarget(SourceLanguage.C);
            NativeTargetDescriptor fixtureTarget = new(new("desktop-static-fixture"), resolved.platformId, resolved.architectureId,
                resolved.abiId, resolved.triple, resolved.toolchain,
                new(false, false), resolved.byteOrder) { sourceConventions = resolved.sourceConventions, headerSearchPaths = resolved.headerSearchPaths };
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "static_fixture", nativeName = nativeName },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false },
                targets = [new() { id = fixtureTarget.targetId.value }],
                api = new() { @namespace = "Generated.StaticFixture", name = "StaticFixtureApi", runtime = new ManagedRuntimeId("fixture.static") },
                runtime = new() { mode = "source" }, cache = new() { enabled = false }};
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).UseFixtureStaticRuntime()
                .RegisterFactory<INativeTargetProvider>(new("fixture/static-target", ExtensionPort.NativeTargetProvider), _ => new StaticFixtureTargetProvider(fixtureTarget)).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, temporary));
            Assert.True(built.success, built.GetDiagnosticSummary());
            AssertConfiguredMetadata(built, fixtureTarget.targetId.value, nativeName);
            string nativeFile = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(Path.Combine(built.outputRoot, fixtureTarget.targetId.value))));
            module = NativeLibrary.Load(nativeFile);
            InvokeGeneratedFromStatic(Path.Combine(built.outputRoot, fixtureTarget.targetId.value, "managed"), module, nativeName);
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Theory]
    [InlineData(BindingImportMode.DllImport, "static_fixture_native")]
    [InlineData(BindingImportMode.DllImport, "__Internal")]
    [InlineData(BindingImportMode.LibraryImport, "static_fixture_native")]
    [InlineData(BindingImportMode.LibraryImport, "__Internal")]
    public async Task StaticTargetPreservesConfiguredDirectImportModule(BindingImportMode importMode, string nativeName)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs static import name " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.h"), "int twice(int value);\n");
            NativeTargetDescriptor resolved = NativeConsumerHarness.ParserTarget(SourceLanguage.C);
            NativeTargetDescriptor fixtureTarget = new(new("desktop-static-fixture"), resolved.platformId, resolved.architectureId,
                resolved.abiId, resolved.triple, resolved.toolchain, new(false, false), resolved.byteOrder) { sourceConventions = resolved.sourceConventions, headerSearchPaths = resolved.headerSearchPaths };
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "static_fixture", nativeName = nativeName },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false },
                targets = [new() { id = fixtureTarget.targetId.value }],
                api = new() { @namespace = "Generated.StaticFixture", name = "StaticFixtureApi", runtime = new ManagedRuntimeId("fixture.static"), importMode = importMode },
                runtime = new() { mode = "source" }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).UseFixtureStaticRuntime()
                .RegisterFactory<INativeTargetProvider>(new("fixture/static-target", ExtensionPort.NativeTargetProvider), _ => new StaticFixtureTargetProvider(fixtureTarget)).Build();
            GenerationRequest request = new(configuration, temporary);
            GenerationResult generated = await host.GenerateAsync(request);
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            AssertConfiguredMetadata(generated, fixtureTarget.targetId.value, nativeName);
            string managedRoot = Path.Combine(generated.outputRoot, fixtureTarget.targetId.value, "managed");
            var imports = Directory.GetFiles(managedRoot, "*.cs", SearchOption.AllDirectories)
                .SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot()
                    .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax>())
                .Where(attribute => attribute.Name.ToString().EndsWith(importMode.ToString(), StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(imports);
            Assert.All(imports, attribute => Assert.Equal(nativeName,
                Assert.IsType<Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax>(attribute.ArgumentList!.Arguments[0].Expression).Token.ValueText));
            await NativeConsumerHarness.CompileManagedAsync(generated, request);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static void AssertConfiguredMetadata(GenerationResult generated, string targetId, string nativeName)
    {
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(generated.outputRoot, targetId, "metadata", "abi.manifest.json")));
        Assert.Equal(nativeName, metadata.RootElement.GetProperty("managedLibrary").GetProperty("nativeName").GetString());
    }

    private static void InvokeGeneratedFromStatic(string managedRoot, nint module, string nativeName)
    {
        var trees = Directory.GetFiles(managedRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new CSharpParseOptions(LanguageVersion.CSharp14), path));
        string[] assemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!.Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("GeneratedStaticFixture_" + Guid.NewGuid().ToString("N"), trees,
            assemblies.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream stream = new();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        AssemblyLoadContext context = new("BGCS.Static.Adapter.Test", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromStream(stream);
            NativeLibrary.SetDllImportResolver(assembly, (name, _, _) =>
            {
                Assert.Equal(nativeName, name);
                return module;
            });
            Type type = assembly.GetType("Generated.StaticFixture.StaticFixtureApi", throwOnError: true)!;
            Assert.Null(type.GetMethod("Load", BindingFlags.Public | BindingFlags.Static));
            using IDisposable api = (IDisposable)type.GetMethod("FromStatic", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
            MethodInfo twice = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance),
                method => method.ReturnType == typeof(int) && method.GetParameters() is [{ ParameterType: var argument }] && argument == typeof(int));
            Assert.Equal(42, twice.Invoke(api, [21]));
            Assert.Equal(-42, twice.Invoke(api, [-21]));
        }
        finally { context.Unload(); }
    }

    private sealed class StaticFixtureTargetProvider(NativeTargetDescriptor descriptor) : INativeTargetProvider
    {
        public bool TryResolve(NativeTargetRequest request, [NotNullWhen(true)] out NativeTargetDescriptor? target)
        {
            target = request.targetId == descriptor.targetId ? descriptor : null;
            return target is not null;
        }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
