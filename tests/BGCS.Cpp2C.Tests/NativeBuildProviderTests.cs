using System;
using System.IO;
using System.Linq;
using BGCS.Cpp2C.Build;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class NativeBuildProviderTests
{
    [Fact]
    public void CMakeProvider_BuildsAndVerifiesGeneratedBridgeOnUnixHost()
    {
        if (OperatingSystem.IsWindows())
            return;
        string compiler = CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp)
            ?? throw new InvalidOperationException("A host C++ compiler is required for the CMake provider test.");
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cmake-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "GeneratedBridge");
        File.WriteAllText(header, "class CMakeDemo { public: int Twice(int value) { return value * 2; } };\n");
        try
        {
            Cpp2CCodeGenerator generator = new(new() { NativeLibraryName = "provider_cmake" });
            generator.Generate(header, output);
            Assert.True(generator.LastResult?.Success);

            string manifestPath = Path.Combine(output, "bridge.manifest.json");
            CppBridgeBuildManifest manifest = CppBridgeBuildManifestSerializer.Load(manifestPath);
            NativeBuildPipeline pipeline = new CMakeNativeBuildProvider("cmake", compiler)
                .CreatePipeline(manifest, manifestPath);
            NativeBuildPipelineResult result = NativeBuildExecutor.Execute(pipeline, TimeSpan.FromMinutes(2));

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Steps.Select(step => step.StandardError)));
            NativeExportInspectionResult exports = NativeExportInspector.Inspect(manifest, manifestPath, result.OutputFile);
            Assert.True(exports.Success, string.Join(", ", exports.Missing));
            Assert.Contains("CMakeDemo_Twice", exports.Actual);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Provider_BuildsGeneratedBridgeAsHostSharedLibrary()
    {
        string compiler = CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp)
            ?? throw new InvalidOperationException("A host C++ compiler is required for the native build provider test.");
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-native-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "GeneratedBridge");
        File.WriteAllText(header, "class Demo { public: int Add(int value) { return value + 1; } };\n");
        try
        {
            Cpp2CGeneratorConfig config = new() { NativeLibraryName = "provider_sample" };
            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);
            Assert.True(generator.LastResult?.Success);

            string manifestPath = Path.Combine(output, "bridge.manifest.json");
            CppBridgeBuildManifest manifest = CppBridgeBuildManifestSerializer.Load(manifestPath);
            NativeBuildPlan plan = new ClangNativeBuildProvider(compiler).CreatePlan(manifest, manifestPath);
            NativeBuildResult result = NativeBuildExecutor.Execute(plan, TimeSpan.FromMinutes(2));

            Assert.True(result.Success, result.StandardOutput + Environment.NewLine + result.StandardError);
            Assert.True(File.Exists(result.OutputFile));
            NativeExportInspectionResult exports = NativeExportInspector.Inspect(manifest, manifestPath, result.OutputFile);
            Assert.True(exports.Success, string.Join(", ", exports.Missing));
            Assert.Contains("DemoCreate", exports.Expected);
            Assert.Contains("Demo_Add", exports.Actual);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Providers_ProduceDeterministicShellIndependentPipelines()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-provider-plans");
        string manifestPath = Path.Combine(root, "bridge.manifest.json");
        CppBridgeBuildManifest unix = CreateManifest("linux-x64-gnu") with
        {
            TargetTriple = "x86_64-linux-gnu",
            TargetSysRoot = "toolchains/linux-sysroot",
            CompilerPath = "clang++"
        };
        CppBridgeBuildManifest windows = CreateManifest("windows-x64-msvc") with
        {
            LinkerArguments = ["/DEBUG:NONE"]
        };

        NativeBuildPipeline cmake = new CMakeNativeBuildProvider("cmake").CreatePipeline(unix, manifestPath);
        NativeBuildPipeline meson = new MesonNativeBuildProvider("meson").CreatePipeline(unix, manifestPath);
        NativeBuildPipeline clangCl = new ClangClNativeBuildProvider("clang-cl").CreatePipeline(windows, manifestPath);
        NativeBuildPipeline msbuild = new MSBuildNativeBuildProvider("msbuild").CreatePipeline(windows, manifestPath);

        Assert.Equal(new[] { "configure", "build" }, cmake.Steps.Select(step => step.Name));
        Assert.Contains("add_library(bgcs_bridge SHARED", Assert.Single(cmake.InputFiles).Content, StringComparison.Ordinal);
        Assert.Contains("-DCMAKE_CXX_COMPILER=clang++", cmake.Steps[0].Arguments);
        Assert.Contains("-DCMAKE_CXX_COMPILER_TARGET=x86_64-linux-gnu", cmake.Steps[0].Arguments);
        Assert.Contains(cmake.Steps[0].Arguments, argument => argument.StartsWith("-DCMAKE_SYSROOT=", StringComparison.Ordinal));
        Assert.Equal(new[] { "setup", "compile", "install" }, meson.Steps.Select(step => step.Name));
        Assert.Contains("shared_library(", meson.InputFiles.Single(input => input.Path.EndsWith("meson.build", StringComparison.Ordinal)).Content, StringComparison.Ordinal);
        Assert.Contains("cpp = 'clang++'", meson.InputFiles.Single(input => input.Path.EndsWith("bgcs-native.ini", StringComparison.Ordinal)).Content, StringComparison.Ordinal);
        Assert.Contains("--native-file", meson.Steps[0].Arguments);
        Assert.Contains("--target=x86_64-linux-gnu", meson.InputFiles.Single(input => input.Path.EndsWith("meson.build", StringComparison.Ordinal)).Content, StringComparison.Ordinal);
        Assert.Contains("/LD", Assert.Single(clangCl.Steps).Arguments);
        Assert.Contains("DynamicLibrary", Assert.Single(msbuild.InputFiles).Content, StringComparison.Ordinal);
        Assert.Contains("/DEBUG:NONE", Assert.Single(msbuild.InputFiles).Content, StringComparison.Ordinal);
        Assert.All(cmake.Steps.Concat(meson.Steps).Concat(clangCl.Steps).Concat(msbuild.Steps),
            step => Assert.DoesNotContain("sh -c", step.Executable + string.Join(' ', step.Arguments), StringComparison.Ordinal));
    }

    [Fact]
    public void ExportInspector_ReadsOnlyDeclaredApiSymbols()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-export-header-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "bridge.h");
        File.WriteAllText(header,
            "#define API(type) type\nAPI(int) Alpha(int value);\nPREFIXAPI(void) Beta(void);\n// API(int) Ignored(void);\n");
        try
        {
            Assert.Equal(new[] { "Alpha", "Beta" }, NativeExportInspector.ReadExpectedSymbols([header]));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static CppBridgeBuildManifest CreateManifest(string target) => new(
        1,
        target,
        null,
        null,
        null,
        "c++23",
        "sample",
        ["src/sample.cpp"],
        ["include/sample.h"],
        ["../sample.hpp"],
        ["include", ".."],
        [],
        ["SAMPLE=1"],
        [],
        [],
        [],
        []);
}
