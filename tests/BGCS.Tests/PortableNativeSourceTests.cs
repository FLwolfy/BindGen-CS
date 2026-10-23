using BGCS.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Artifacts;
using BGCS.Core.Inputs;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Tests;

public sealed class PortableNativeSourceTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CCompute(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CppCompute(
        int value,
        out int result,
        out nint error
    );
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint StaticGetExport(uint index);

    [Theory]
    [InlineData(SourceLanguage.C, false)]
    [InlineData(SourceLanguage.Cpp, false)]
    [InlineData(SourceLanguage.Cpp, true)]
    public async Task RelocatedSourcesCompileAndInvokeAfterOriginalInputsAreHidden(
        SourceLanguage language,
        bool staticAdapter
    ) {
        using Scratch scratch = new();
        string header = Path.Combine(scratch.source, "headers", "public", "native.h");
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(header)!, "detail"));
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(header)!, "detail", "value.h"), "#define PORTABLE_VALUE 1\n");
        await File.WriteAllTextAsync(header, "#pragma once\n#include \"detail/value.h\"\nstatic inline int compute(int value) { return value + PORTABLE_VALUE; }\n");
        BindingConfiguration configuration = scratch.Configuration(language) with
        {
            source = new() { language = language, headers = ["headers/public/native.h"], includeDirectories = ["headers"], includeHeaderHelpers = language == SourceLanguage.C }
        };
        NativeTargetDescriptor? staticTarget = null;
        if (staticAdapter)
        {
            NativeTargetDescriptor resolved = NativeConsumerHarness.ParserTarget(language);
            staticTarget = new(new("portable-static-fixture"), resolved.platformId, resolved.architectureId,
                resolved.abiId, resolved.triple, resolved.toolchain, new(false, false), resolved.byteOrder)
            { sourceConventions = resolved.sourceConventions, headerSearchPaths = resolved.headerSearchPaths };
            configuration = configuration with
            {
                targets = [new() { id = staticTarget.targetId.value }],
                api = configuration.api with { runtime = new ManagedRuntimeId("fixture.static") }
            };
        }
        await using BindingHost host = Host(staticTarget);
        GenerationResult result = await host.GenerateAsync(new(configuration, scratch.source));
        Assert.True(result.success, result.GetDiagnosticSummary());
        NativeSourceManifest manifest = Assert.Single(result.nativeSources);
        Assert.Equal(staticAdapter, manifest.staticExportTable is not null);
        NativeSourceManifest disk = Assert.Single(BindingArtifactManifest.ReadNativeSources(result.outputRoot, "portable", TestHostPaths.Create()));
        Assert.Equal(JsonSerializer.Serialize(manifest), JsonSerializer.Serialize(disk));
        Assert.NotEmpty(manifest.sources);
        Assert.All(manifest.sources, source => Assert.Contains(Path.GetExtension(source.relativePath), new[] { ".c", ".cpp", ".cc", ".cxx" }));
        Assert.All(manifest.headers, source => Assert.Contains(Path.GetExtension(source.relativePath), new[] { ".h", ".hpp", ".hh", ".hxx" }));
        Assert.Contains(manifest.inputs, input => input.relativePath.EndsWith("detail/value.h", StringComparison.Ordinal));
        Assert.DoesNotContain(scratch.source.Replace('\\', '/'), JsonSerializer.Serialize(manifest).Replace("\\\\", "/"), StringComparison.OrdinalIgnoreCase);
        foreach (ArtifactDescriptor source in manifest.sources)
            Assert.DoesNotContain(scratch.source.Replace('\\', '/'), (await File.ReadAllTextAsync(Path.Combine(result.outputRoot, source.relativePath))).Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

        string frozen = Path.Combine(scratch.root, "冻结 inputs");
        string relocated = Path.Combine(scratch.root, "relocated output");
        CopyTree(scratch.source, frozen);
        CopyTree(result.outputRoot, relocated);
        Directory.Move(scratch.source, scratch.source + ".hidden");
        Dictionary<string, string> roots = result.nativeInputRoots.Where(binding => binding.targetId == manifest.targetId).ToDictionary(binding => binding.rootId, binding =>
        {
            if (binding.rootId == "generated") return relocated;
            if (binding.directory == scratch.source || binding.directory.StartsWith(scratch.source + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return Path.GetFullPath(Path.GetRelativePath(scratch.source, binding.directory), frozen);
            return binding.directory;
        }, StringComparer.Ordinal);
        _ = Assert.Single(BindingArtifactManifest.ReadNativeSources(relocated, "portable", TestHostPaths.Create()));
        List<string> arguments = manifest.defines.Select(define => "-D" + define).ToList();
        arguments.AddRange(NativeConsumerHarness.ManifestSystemIncludeArguments(manifest, roots, Assert.Single(result.targetModels).resolvedTarget));
        string binary = Path.Combine(scratch.root, OperatingSystem.IsWindows() ? "portable.dll" : OperatingSystem.IsMacOS() ? "libportable.dylib" : "libportable.so");
        await NativeConsumerHarness.CompileSharedAsync(manifest.sources.Select(source => Path.Combine(relocated, source.relativePath)), binary, frozen,
            arguments, manifest.includeDirectories.Where(include => !include.isSystem).Select(include => Path.Combine(roots[include.rootId], include.relativePath)),
            manifest.languageStandard, Assert.Single(result.targetModels).resolvedTarget);
        nint library = NativeLibrary.Load(binary);
        try
        {
            string export = Assert.Single(Assert.Single(result.targetModels).interop.functions, function => function.sourceName == "compute").exportName;
            NativeExportBinding binding = Assert.Single(manifest.exportBindings, binding => binding.exportName == export);
            Assert.Equal("portable", binding.moduleId);
            Assert.Equal("portable", manifest.module.id);
            Assert.NotNull(manifest.runtimeRequirements);
            nint address = NativeLibrary.GetExport(library, export);
            if (manifest.staticExportTable is { } table)
            {
                var getExport = Marshal.GetDelegateForFunctionPointer<StaticGetExport>(NativeLibrary.GetExport(library, table.getExportSymbol));
                Assert.Equal(address, unchecked((nint)getExport(checked((uint)table.exportNames.IndexOf(export)))));
            }
            if (language == SourceLanguage.C) Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<CCompute>(address)(41));
            else
            {
                Assert.Equal(0, Marshal.GetDelegateForFunctionPointer<CppCompute>(address)(41, out int value, out nint error));
                Assert.Equal(42, value);
                Assert.Equal(0, error);
            }
        }
        finally { NativeLibrary.Free(library); }
    }

    [Fact]
    public async Task SameNamedHeadersUseVerifiedQualifiedIncludesAndRootOrderInvalidatesCache()
    {
        using Scratch scratch = new();
        foreach (string name in new[] { "first", "second" })
        {
            Directory.CreateDirectory(Path.Combine(scratch.source, name));
            await File.WriteAllTextAsync(Path.Combine(scratch.source, name, "shared.h"), "#pragma once\ninline int " + name + "_value() { return 42; }\n");
        }
        BindingConfiguration configuration = scratch.Configuration(SourceLanguage.Cpp) with
        {
            source = new() { language = SourceLanguage.Cpp, headers = ["first/shared.h", "second/shared.h"], includeDirectories = ["first", "second"] }
        };
        await using BindingHost host = Host();
        GenerationResult cold = await host.GenerateAsync(new(configuration, scratch.source));
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        NativeSourceManifest manifest = Assert.Single(cold.nativeSources);
        Assert.Equal(2, manifest.headerReferences.Select(header => header.include).Distinct().Count());
        Assert.Contains(manifest.headerReferences, header => header.include == "second/shared.h");
        GenerationResult warm = await host.GenerateAsync(new(configuration, scratch.source));
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        Assert.Equal(JsonSerializer.Serialize(cold.nativeSources), JsonSerializer.Serialize(warm.nativeSources));
        Assert.Contains(warm.cacheStages, stage => stage.status == "hit");
        GenerationResult reordered = await host.GenerateAsync(new(configuration with { source = configuration.source with { includeDirectories = ["second", "first"] } }, scratch.source));
        Assert.True(reordered.success, reordered.GetDiagnosticSummary());
        Assert.Contains(Assert.Single(reordered.nativeSources).headerReferences, header => header.include == "first/shared.h");
        Assert.DoesNotContain(reordered.cacheStages, stage => stage.status == "hit");
    }

    [Fact]
    public async Task UnmappedInputOrAbsoluteUserIncludeDoesNotReplacePublishedOutput()
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.source, "native.h"), "inline int compute() { return 42; }\n");
        BindingConfiguration configuration = scratch.Configuration(SourceLanguage.Cpp);
        await using BindingHost host = Host();
        GenerationResult original = await host.GenerateAsync(new(configuration, scratch.source));
        Assert.True(original.success, original.GetDiagnosticSummary());
        string owner = await File.ReadAllTextAsync(Path.Combine(original.outputRoot, ".bgcs-artifacts.json"));
        string external = Path.Combine(scratch.root, "external.h");
        await File.WriteAllTextAsync(external, "int external(void);\n");
        string detail = Path.Combine(scratch.source, "detail.h");
        await File.WriteAllTextAsync(detail, "#pragma once\nconstexpr int detail = 42;\n");
        foreach (SourceConfiguration source in new[]
        {
            configuration.source with { headers = [external] },
            configuration.source with { preamble = "#include \"" + external.Replace('\\', '/') + "\"\n" },
            configuration.source with { preamble = "#/**/include/**/\"" + external.Replace('\\', '/') + "\"\n" },
            configuration.source with { postamble = "constexpr int numeric = 1'000;\n#include \"" + detail.Replace('\\', '/') + "\"\n" }
        })
        {
            GenerationResult failed = await host.GenerateAsync(new(configuration with { source = source }, scratch.source));
            Assert.False(failed.success);
            Assert.False(failed.published);
            if (source.postamble.Contains("1'000", StringComparison.Ordinal))
                Assert.Contains(failed.diagnostics, diagnostic => diagnostic.message.Contains("nonportable absolute include", StringComparison.Ordinal));
            Assert.Empty(failed.nativeSources);
            Assert.Empty(failed.nativeInputRoots);
            Assert.Equal(owner, await File.ReadAllTextAsync(Path.Combine(original.outputRoot, ".bgcs-artifacts.json")));
            Assert.NotEmpty(BindingArtifactManifest.ReadNativeSources(original.outputRoot, "portable", TestHostPaths.Create()));
        }
    }

    [Fact]
    public async Task CManifestKeepsTheParsersCSearchOrderWhenCppSdkHeadersWouldShadowIt()
    {
        using Scratch scratch = new();
        string cRoot = Path.Combine(scratch.source, "sdk-c"), cppRoot = Path.Combine(scratch.source, "sdk-cpp");
        Directory.CreateDirectory(cRoot);
        Directory.CreateDirectory(cppRoot);
        await File.WriteAllTextAsync(Path.Combine(cRoot, "choice.h"), "#define SELECTED_VALUE 1\n");
        await File.WriteAllTextAsync(Path.Combine(cppRoot, "choice.h"), "#error C++ SDK headers must not enter this C search\n");
        await File.WriteAllTextAsync(Path.Combine(scratch.source, "native.h"), "#include <choice.h>\nstatic inline int choose(int value) { return value + SELECTED_VALUE; }\n");
        BindingConfiguration configuration = scratch.Configuration(SourceLanguage.C) with
        {
            source = new() { language = SourceLanguage.C, headers = ["native.h"], includeHeaderHelpers = true },
            targets = [new() { id = "host", settings = new() { systemIncludeDirectories = [cRoot], cxxSystemIncludeDirectories = [cppRoot] } }]
        };
        await using BindingHost host = Host();
        GenerationResult result = await host.GenerateAsync(new(configuration, scratch.source));
        Assert.True(result.success, result.GetDiagnosticSummary());
        NativeSourceManifest manifest = Assert.Single(result.nativeSources);
        Dictionary<string, string> roots = result.nativeInputRoots.ToDictionary(binding => binding.rootId, binding => binding.directory);
        Assert.DoesNotContain(result.nativeInputRoots, binding => binding.directory == cppRoot);
        string binary = Path.Combine(scratch.root, OperatingSystem.IsWindows() ? "choices.dll" : OperatingSystem.IsMacOS() ? "libchoices.dylib" : "libchoices.so");
        await NativeConsumerHarness.CompileSharedAsync(manifest.sources.Select(source => Path.Combine(result.outputRoot, source.relativePath)), binary, scratch.source,
            includeDirectories: manifest.includeDirectories.Select(include => Path.Combine(roots[include.rootId], include.relativePath)),
            languageStandard: manifest.languageStandard, parserTarget: Assert.Single(result.targetModels).resolvedTarget);
        nint library = NativeLibrary.Load(binary);
        try
        {
            string export = Assert.Single(Assert.Single(result.targetModels).interop.functions, function => function.sourceName == "choose").exportName;
            Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<CCompute>(NativeLibrary.GetExport(library, export))(41));
        }
        finally { NativeLibrary.Free(library); }
    }

    [Fact]
    public async Task CommentsAndStringLiteralsAreNotMistakenForAbsoluteIncludes()
    {
        using Scratch scratch = new();
        await File.WriteAllTextAsync(Path.Combine(scratch.source, "native.h"), "inline int compute() { return 42; }\n");
        BindingConfiguration configuration = scratch.Configuration(SourceLanguage.Cpp);
        configuration = configuration with
        {
            source = configuration.source with
            {
                preamble = "/* #include \"C:/comment.h\" */\n// #include \"/comment.h\"\n",
                postamble = "static constexpr int numeric = 1'000 + 0x1'AB;\nstatic constexpr const char* example = R\"sample(\n#include \"C:/string.h\"\n)sample\";\n"
            }
        };
        await using BindingHost host = Host();
        GenerationResult result = await host.GenerateAsync(new(configuration, scratch.source));
        Assert.True(result.success, result.GetDiagnosticSummary());
    }

    private static BindingHost Host(NativeTargetDescriptor? target = null)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        BindingHostBuilder builder = BindingGenerator.CreateBuilder().UseWorkerDirectory(Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0"));
        if (target is not null) builder.UseFixtureStaticRuntime();
        if (target is not null) builder.RegisterFactory<INativeTargetProvider>(new("fixture/portable-static", ExtensionPort.NativeTargetProvider), _ => new StaticTargetProvider(target));
        return builder.Build();
    }

    private sealed class StaticTargetProvider(NativeTargetDescriptor descriptor) : INativeTargetProvider
    {
        public bool TryResolve(
            NativeTargetRequest request,
            [NotNullWhen(true)] out NativeTargetDescriptor? target
        ) {
            target = request.targetId == descriptor.targetId ? descriptor : null;
            return target is not null;
        }
    }

    private static void CopyTree(
        string source,
        string destination
    ) {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string copy = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    private sealed class Scratch : IDisposable
    {
        internal readonly string root = Path.Combine(Path.GetTempPath(), "bgcs-portable-" + Guid.NewGuid().ToString("N"));
        internal string source => Path.Combine(root, "source");
        internal Scratch() => Directory.CreateDirectory(source);
        internal BindingConfiguration Configuration(SourceLanguage language) => new()
        {
            schemaVersion = 2,
            library = new() { id = "portable", nativeName = "portable" },
            source = new() { language = language, headers = ["native.h"] },
            api = new() { @namespace = "Portable.Bindings", name = "NativeApi" },
            output = new() { root = Path.Combine(root, "Generated") },
            cache = new() { directory = Path.Combine(root, "cache") }
        };
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
