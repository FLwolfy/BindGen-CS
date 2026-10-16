using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Assets;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class UnixNativeProviderExecutionTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RoundTrip(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint TextValue();

    [Theory]
    [InlineData("clang")]
    [InlineData("gnu")]
    [InlineData("cmake")]
    [InlineData("meson")]
    [Trait("Category", "UnixNativeProvider")]
    public async Task ProvidersCompileMixedC17Cpp23AndInvokeUsingFrozenWorkingDirectory(string providerId)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() || providerId == "gnu" && !OperatingSystem.IsLinux()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs Unix provider 验收 & 50% " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint module = 0;
        try
        {
            string c = Path.Combine(root, "C source & test.c"), cpp = Path.Combine(root, "C++ source ' test.cpp"), header = Path.Combine(root, "native only header.hpp");
            string origin = Path.Combine(root, "arguments 目录 & 50%"), scratch = Path.Combine(root, "scratch");
            Directory.CreateDirectory(origin);
            Directory.CreateDirectory(scratch);
            string response = Path.Combine(origin, "driver.rsp");
            await File.WriteAllTextAsync(response, "-DNATIVE_ARGUMENT_FACTOR=13\n");
            await File.WriteAllTextAsync(Path.Combine(origin, "native_argument_header.h"), "#define NATIVE_ARGUMENT_HEADER 17\n");
            await File.WriteAllTextAsync(header, "inline constexpr int native_increment = 7;\nextern \"C\" int native_single_header_definition(int value) { return value + native_increment; }\n");
            string facts = Path.Combine(root, "facts.input.hpp");
            await File.WriteAllTextAsync(facts, new NativeFactInputEmitter().Emit([header]));
            await File.WriteAllTextAsync(c, """
                #include <stdint.h>
                #include "native_argument_header.h"
                _Static_assert(__STDC_VERSION__ >= 201710L, "C17");
                _Static_assert(NATIVE_ARGUMENT_HEADER == 17, "explicit include origin");
                _Static_assert(NATIVE_ARGUMENT_FACTOR == 13, "relative response origin");
                const char* native_quoted(void) { return NATIVE_ARGUMENT_TEXT; }
                int native_roundtrip(int value) { return value * 2; }
                """);
            await File.WriteAllTextAsync(cpp, """
                #include <vector>
                #include "native only header.hpp"
                static_assert(__cplusplus > 202002L, "C++23");
                extern "C" int native_cpp_roundtrip(int value) { std::vector<int> data{value, native_increment}; return data[0] + data[1]; }
                """);
            string driver = providerId == "clang" ? "clang++" : OperatingSystem.IsLinux() ? "g++" : "clang++";
            string compiler = ToolchainDiscovery.FindCompiler(SourceLanguage.Cpp, driver)
                ?? throw new InvalidOperationException("The neutral Unix provider gate requires the explicitly selected installed compiler: " + driver);
            NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"), new(compiler))), SourceLanguage.Cpp);
            NativeBuildRequest request = new(target, [new(c, SourceLanguage.C, "c17"), new(cpp, SourceLanguage.Cpp, "c++23")], scratch,
                Path.Combine(scratch, NativeOutputLayout.ResolveFilename("fixture", target, NativeOutputKind.SharedLibrary)))
            {
                argumentWorkingDirectory = origin,
                preprocessingSources = [new(facts, SourceLanguage.Cpp, "c++23")],
                includeDirectories = [origin + Path.DirectorySeparatorChar],
                defines = ["NATIVE_ARGUMENT_TEXT=\"A\\042B\\134C&50%目录\""],
                compilerArguments = ["@driver.rsp"]
            };
            NativeBuildPlan plan = new NativeBuildProviderResolver().Resolve(target, providerId).Plan(request, CancellationToken.None);
            Assert.Equal(2, plan.dependencyFiles.Length);
            Assert.Equal(facts, Assert.Single(plan.preprocessingOperations, operation => operation.preprocessingOnly).source);
            int verified = 0;
            NativeBuildExecutionResult result = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(plan, TimeSpan.FromMinutes(3),
                sourceVerifier: async (operation, token) =>
                {
                    string source = await File.ReadAllTextAsync(operation.outputFile, token);
                    Assert.Contains(operation.source == c ? "native_roundtrip" : "native_cpp_roundtrip", source);
                    Assert.DoesNotContain("#include \"native only header.hpp\"", source);
                    Assert.False(NativeEffectiveFactArguments.Resolve(plan, operation, token).IsDefault);
                    verified++;
                }, verificationSources: [c, cpp]);
            Assert.Equal(2, verified);
            Assert.All(result.executions, execution => Assert.Equal(0, execution.exitCode));
            Assert.Contains(result.inputSnapshot.files, input => input.path == header);
            Assert.Contains(result.inputSnapshot.files, input => input.path == facts);
            Assert.Contains(result.argumentInputs.files, input => input.path == response);
            Assert.True(SourceSnapshotVerifier.Matches(result.inputSnapshot));
            Assert.True(SourceSnapshotVerifier.Matches(result.argumentInputs));
            var exports = await NativeExportInspector.InspectAsync(target, plan.outputFile, ["native_roundtrip", "native_cpp_roundtrip", "native_quoted"]);
            Assert.True(exports.success);
            Assert.True(Path.IsPathFullyQualified(exports.tool));
            module = NativeLibrary.Load(plan.outputFile);
            Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<RoundTrip>(NativeLibrary.GetExport(module, "native_roundtrip"))(21));
            Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<RoundTrip>(NativeLibrary.GetExport(module, "native_cpp_roundtrip"))(35));
            Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<RoundTrip>(NativeLibrary.GetExport(module, "native_single_header_definition"))(35));
            Assert.Equal("A\"B\\C&50%目录", Marshal.PtrToStringUTF8(Marshal.GetDelegateForFunctionPointer<TextValue>(NativeLibrary.GetExport(module, "native_quoted"))()));
            await File.AppendAllTextAsync(header, "// independent compiler input mutation\n");
            Assert.False(SourceSnapshotVerifier.Matches(result.inputSnapshot));
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            Directory.Delete(root, recursive: true);
        }
    }
}
