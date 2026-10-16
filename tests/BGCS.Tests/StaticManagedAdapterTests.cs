using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class StaticManagedAdapterTests
{
    [Fact]
    public async Task GeneratedFromStaticValidatesProtocolAndInvokesRealNativeFunctionThroughItsAddressTable()
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
            NativeTargetDescriptor hostTarget = new TargetResolver().Resolve(new(new("host")));
            NativeTargetDescriptor resolved = ToolchainResolver.ResolveToolchain(hostTarget, SourceLanguage.C, OperatingSystem.IsWindows() ? "msvc" : null);
            NativeTargetDescriptor fixtureTarget = new(new("desktop-static-fixture"), resolved.platformId, resolved.architectureId,
                resolved.abiId, resolved.triple, resolved.toolchain,
                new([NativeOutputKind.SharedLibrary], NativeOutputKind.SharedLibrary, false, false, "fixture-static-adapter"), resolved.byteOrder);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1,
                library = new() { id = "static_fixture", nativeName = "static_fixture_native" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false },
                targets = [new() { id = fixtureTarget.targetId.value }],
                api = new() { @namespace = "Generated.StaticFixture", name = "StaticFixtureApi", runtime = ManagedRuntimeKind.NativeAot },
                runtime = new() { mode = "source" }, cache = new() { enabled = false },
                nativeBuild = new() { mode = "sources", sources = ["native.c"], outputKind = NativeOutputKind.SharedLibrary }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot())
                .AddTargetProvider("fixture/static-target", () => new StaticFixtureTargetProvider(fixtureTarget)).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            string nativeFile = Assert.Single(Directory.GetFiles(Path.Combine(built.outputRoot, fixtureTarget.targetId.value, "native", "bin")));
            module = NativeLibrary.Load(nativeFile);
            InvokeGeneratedFromStatic(Path.Combine(built.outputRoot, fixtureTarget.targetId.value, "managed"), module);
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static void InvokeGeneratedFromStatic(string managedRoot, nint module)
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
                Assert.Equal("__Internal", name);
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
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
