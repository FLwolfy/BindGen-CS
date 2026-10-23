using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Analysis.Bridge;
using BGCS.Configuration;
using BGCS.Core.Bridge;
using BGCS.Core.Inputs;
using BGCS.Infrastructure.Execution;
using BGCS.Targets.Desktop.Toolchains;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeMsvcStandardLibraryTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Construct(out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Getter(nint self, out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntegerValue(nint self, out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(nint self, out ulong result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Destroy(nint self, out nint error);

    [Fact]
    [Trait("Category", "WindowsNativeProvider")]
    public async Task MsvcBuildsAndInvokesUserClassWithStringVectorAndOptionalStorage()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs MSVC STL effective facts 目录 & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint library = 0, instance = 0;
        BridgeExport? destructor = null;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "box.hpp"), Source);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "msvc_stl", nativeName = "msvc_stl_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["box.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                runtime = new() { mode = "source" }, cache = new() { enabled = false }};
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationRequest request = new(configuration, root);
            GenerationResult built = await host.GenerateAsync(request);
            Assert.True(built.success, built.GetDiagnosticSummary());
            await NativeConsumerHarness.CompileManagedAsync(built, request);
            string module = Assert.Single(Directory.GetFiles(built.outputRoot, "bridge.module.json", SearchOption.AllDirectories));
            NativeBridgePlan bridge = JsonSerializer.Deserialize<NativeBridgePlan>(await File.ReadAllTextAsync(module))!;
            BridgeExport create = Assert.Single(bridge.exports, export => export.operation.kind == BridgeOperationKind.Construct
                && bridge.source.GetType(export.operation.ownerType!.Value).spelling == "Fixture::Box");
            destructor = Assert.Single(bridge.exports, export => export.id == create.resultRelease);
            string targetRoot = Path.Combine(built.outputRoot, Assert.Single(built.targetModels).targetId);
            string nativeDirectory = NativeConsumerHarness.NativeDirectory(targetRoot);
            string intermediates = Path.Combine(nativeDirectory, "obj");
            Directory.CreateDirectory(intermediates);
            string binary = Path.Combine(nativeDirectory, "msvc_stl_bridge.dll");
            string architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64", Architecture.X86 => "x86", Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException("The MSVC fixture requires a supported Windows process architecture.")
            };
            string compiler = MsvcToolchainDiscovery.FindCompiler(architecture) ?? throw new FileNotFoundException("The fixture requires the actual installed MSVC compiler.");
            MsvcToolchainInfo sdk = MsvcToolchainDiscovery.Resolve(compiler, architecture);
            string tools = Path.GetDirectoryName(compiler)!;
            Assert.True(File.Exists(Path.Combine(tools, "link.exe")), "The selected MSVC toolset has no matching linker.");
            string toolset = Directory.GetParent(tools)!.Parent!.Parent!.FullName;
            List<string> arguments = ["/nologo", "/LD", "/EHsc", "/std:c++latest", "/MD", "/TP", "/utf-8", "/Zc:__cplusplus", "/Zc:preprocessor",
                "/Fo" + intermediates + Path.DirectorySeparatorChar, "/Fd" + Path.Combine(intermediates, "compiler.pdb"), "/Fe" + binary,
                "/I" + root, "/I" + Path.Combine(targetRoot, "native", "include")];
            arguments.AddRange(sdk.includeDirectories.Select(path => "/I" + path));
            arguments.AddRange(built.artifacts.Where(artifact => artifact.mediaType == "text/x-c++")
                .Select(artifact => Path.Combine(built.outputRoot, artifact.relativePath)));
            arguments.AddRange(["/link", "/LIBPATH:" + Path.Combine(toolset, "lib", architecture),
                "/LIBPATH:" + Path.Combine(sdk.sdkRoot, "Lib", sdk.sdkVersion, "ucrt", architecture),
                "/LIBPATH:" + Path.Combine(sdk.sdkRoot, "Lib", sdk.sdkVersion, "um", architecture),
                "/IMPLIB:" + Path.Combine(intermediates, "fixture.lib"), "/PDB:" + Path.Combine(intermediates, "link.pdb")]);
            ImmutableDictionary<string, string?> environment = ImmutableDictionary<string, string?>.Empty
                .Add("INCLUDE", null).Add("LIB", null).Add("CL", null).Add("_CL_", null).Add("LINK", null).Add("_LINK_", null)
                .Add("PATH", tools + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? ""));
            var compiled = await ProcessExecutor.ExecuteAsync(compiler, arguments, root, TimeSpan.FromMinutes(3), environmentOverrides: environment);
            Assert.False(compiled.timedOut);
            Assert.True(compiled.exitCode == 0, compiled.standardOutput + Environment.NewLine + compiled.standardError);
            Assert.True(File.Exists(binary), binary);
            library = NativeLibrary.Load(binary);
            T Function<T>(BridgeExport export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export.name));
            BridgeExport Method(string name) => Assert.Single(bridge.exports, export => export.operation.declaration is { } declaration
                && bridge.source.GetDeclaration(declaration).qualifiedName == "Fixture::Box::" + name);
            Assert.Equal(0, Function<Construct>(create)(out instance, out nint error));
            Assert.NotEqual(0, instance);
            Assert.Equal(0, error);
            Assert.Equal(0, Function<IntegerValue>(Method("Read"))(instance, out int total, out error));
            Assert.Equal(42, total);
            Assert.Equal(0, error);
            foreach (string name in new[] { "Text", "Values", "Optional" })
            {
                BridgeExport getter = Method(name);
                Assert.Equal(0, Function<Getter>(getter)(instance, out nint holder, out error));
                Assert.NotEqual(0, holder);
                Assert.Equal(0, error);
                try
                {
                    var type = StandardLibraryFactReader.Resolve(bridge.source, getter.nativeResultType!.Value).id;
                    BridgeExport access = Assert.Single(bridge.exports, export => export.operation.ownerType == type
                        && export.operation.standardValue?.action == (name == "Optional" ? StandardValueAction.GetValue : StandardValueAction.Count));
                    if (name == "Optional")
                    {
                        Assert.Equal(0, Function<IntegerValue>(access)(holder, out int value, out error));
                        Assert.Equal(6, value);
                    }
                    else
                    {
                        Assert.Equal(0, Function<Count>(access)(holder, out ulong count, out error));
                        Assert.Equal(3UL, count);
                    }
                    Assert.Equal(0, error);
                }
                finally { Assert.Equal(0, Function<Destroy>(Assert.Single(bridge.exports, export => export.id == getter.resultRelease))(holder, out error)); }
            }
        }
        finally
        {
            if (instance != 0 && library != 0 && destructor is not null)
                Assert.Equal(0, Marshal.GetDelegateForFunctionPointer<Destroy>(NativeLibrary.GetExport(library, destructor.name))(instance, out _));
            if (library != 0) NativeLibrary.Free(library);
            Directory.Delete(root, recursive: true);
        }
    }

    internal const string Source = """
        #pragma once
        #include <string>
        #include <vector>
        #include <optional>
        namespace Fixture {
        class Box {
            std::string m_text;
            std::vector<int> m_values;
            std::optional<int> m_optional;
        public:
            Box() : m_text("ABC"), m_values{10, 11, 12}, m_optional(6) {}
            ~Box() noexcept = default;
            int Read() const { int total = static_cast<int>(m_text.size()) + m_optional.value(); for (int value : m_values) total += value; return total; }
            std::string Text() const { return m_text; }
            std::vector<int> Values() const { return m_values; }
            std::optional<int> Optional() const { return m_optional; }
        };
        }
        """;

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
