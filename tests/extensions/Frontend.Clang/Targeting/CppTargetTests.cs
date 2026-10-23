using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Extension.BrowserWasm;
using BGCS.Infrastructure.IO;
using BGCS.Targets.Desktop;
using BGCS.Targets.Desktop.Targeting;
using BGCS.Targets.Desktop.Toolchains;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class CppTargetTests : FrontendTestHost
{
    [Fact]
    public async Task SelectedAppleFrameworkAndCHeadersAreParsedAndSnapshottedExplicitly()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-explicit-framework-" + Guid.NewGuid().ToString("N"));
        string sdk = Path.Combine(root, "sdk"), includes = Path.Combine(sdk, "usr", "include");
        string frameworks = Path.Combine(sdk, "System", "Library", "Frameworks");
        string frameworkHeaders = Path.Combine(frameworks, "BgcsFixture.framework", "Headers");
        Directory.CreateDirectory(includes); Directory.CreateDirectory(frameworkHeaders);
        try
        {
            File.WriteAllText(Path.Combine(includes, "sdk_value.h"), "typedef int bgcs_sdk_value;\n");
            string frameworkHeader = Path.Combine(frameworkHeaders, "value.h");
            File.WriteAllText(frameworkHeader, "typedef int bgcs_framework_value;\n");
            string header = Path.Combine(root, "entry.h");
            File.WriteAllText(header, "#include <sdk_value.h>\n#include <BgcsFixture/value.h>\nbgcs_sdk_value read_value(bgcs_framework_value value);\n");
            Assert.True(new DesktopTargetProvider().TryResolve(new(new("macos-x64-darwin"), new(sysRoot: sdk)), out NativeTargetDescriptor? target));
            FrontendResult parsed = await new ClangFrontend(host).ParseAsync(new(target!, SourceLanguage.C, [header])
            { pathBase = root, parseMacros = false, headerSearchPaths = target!.headerSearchPaths }, CancellationToken.None);
            Assert.DoesNotContain(parsed.module.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Contains(parsed.module.declarations, declaration => declaration.name == "read_value");
            Assert.Contains(parsed.snapshot.searchDirectories, directory => directory.path == frameworks);
            Assert.Contains(parsed.snapshot.files, file => file.path == frameworkHeader);
            Assert.True(SourceSnapshotVerifier.Matches(parsed.snapshot, host.paths));
            File.WriteAllText(frameworkHeader, "typedef float bgcs_framework_value;\n");
            Assert.False(SourceSnapshotVerifier.Matches(parsed.snapshot, host.paths));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public async Task CppSdkWrappersPrecedeCHeadersAndIncludeNextReachesTheCAbi()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-include-order-" + Guid.NewGuid().ToString("N"));
        string c = Path.Combine(root, "c"), cxx = Path.Combine(root, "cxx");
        Directory.CreateDirectory(c); Directory.CreateDirectory(cxx);
        File.WriteAllText(Path.Combine(c, "stdint.h"), "#ifndef __CLANG_STDINT_H\n#error Parser builtin headers were bypassed\n#endif\n#define BGCS_C_HEADER 1\ntypedef unsigned int bgcs_uint;\n");
        File.WriteAllText(Path.Combine(cxx, "stdint.h"), "#define BGCS_CXX_HEADER 1\n#include_next <stdint.h>\n");
        try
        {
            NativeToolchainDescriptor toolchain = new(systemIncludeFolders: [c], cxxSystemIncludeFolders: [cxx]);
            Assert.True(new DesktopTargetProvider().TryResolve(new(new("linux-x64-gnu"), toolchain) { language = SourceLanguage.Cpp }, out NativeTargetDescriptor? target));
            FrontendResult result = await ParseSourceAsync("#include <stdint.h>\n#ifndef BGCS_CXX_HEADER\n#error C++ wrappers bypassed\n#endif\n#ifndef BGCS_C_HEADER\n#error C ABI bypassed\n#endif\nstruct AbiValue { bgcs_uint value; };", target);
            Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration record = Assert.Single(result.module.declarations, value => value.name == "AbiValue");
            Assert.Equal(4, result.module.GetType(record.type!.Value).size);
            Assert.Equal(new[] { cxx, c }, target!.headerSearchPaths.Select(value => value.path));
            Assert.True(new DesktopTargetProvider().TryResolve(new(new("linux-x64-gnu"), toolchain) { language = SourceLanguage.C }, out NativeTargetDescriptor? cTarget));
            Assert.Equal(new[] { c }, cTarget!.headerSearchPaths.Select(value => value.path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Theory]
    [InlineData("windows-x64-msvc", "x86_64-pc-windows-msvc")]
    [InlineData("linux-x64-gnu", "x86_64-unknown-linux-gnu")]
    [InlineData("linux-arm64-musl", "aarch64-unknown-linux-musl")]
    [InlineData("macos-arm64-darwin", "arm64-apple-darwin")]
    [InlineData("emscripten-wasm32-emscripten", "wasm32-unknown-emscripten")]
    public void ResolveExplicitTargetProducesStableTripleAndIdentifier(
        string id,
        string triple
    ) {
        NativeTargetDescriptor target = Resolve(id);
        Assert.Equal(id, target.targetId.value); Assert.Equal(triple, target.triple);
    }
    [Fact]
    public void ResolveInvalidAbiFailsClearly() => Assert.Throws<ArgumentException>(() => new TargetResolver().Resolve(new(new("macos-arm64-msvc"))));
    [Fact]
    public async Task ParseEmscriptenStructureUsesWasm32PointerLayout()
    {
        NativeModule module = (await ParseSourceAsync("struct BrowserAbi { void* handle; unsigned long count; };", Resolve("emscripten-wasm32-emscripten"), SourceLanguage.C)).module;
        NativeDeclaration record = Assert.Single(module.declarations, value => value.name == "BrowserAbi");
        Assert.Equal(4, module.pointerSize); Assert.Equal(8, module.GetType(record.type!.Value).size);
        Assert.All(record.children.Select(module.GetDeclaration), field => Assert.Equal(4, module.GetType(field.type!.Value).size));
    }
    [Fact]
    public async Task RetargetingReplacesArchitectureMacrosWithoutTouchingUserDefines()
    {
        const string source = "#ifndef USER_FEATURE\n#error missing user feature\n#endif\n#ifdef _WIN64\nstruct Selected { int x64; };\n#else\nstruct Selected { int x86; };\n#endif";
        NativeModule x64 = (await ParseSourceAsync(source, Resolve("windows-x64-msvc"), defines: ["USER_FEATURE=1"])).module;
        NativeModule x86 = (await ParseSourceAsync(source, Resolve("windows-x86-msvc"), defines: ["USER_FEATURE=1"])).module;
        Assert.DoesNotContain(x64.diagnostics.Concat(x86.diagnostics), value => value.isFailure);
        Assert.Contains(x64.declarations, value => value.name == "x64"); Assert.Contains(x86.declarations, value => value.name == "x86");
        Assert.DoesNotContain(x86.declarations, value => value.name == "x64");
    }
    [Fact]
    public async Task RetargetingDropsPreviousSysroot()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-retarget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "usr", "include"));
        try
        {
            File.WriteAllText(Path.Combine(root, "usr", "include", "only_selected.h"), "typedef int selected_value;");
            Assert.True(new DesktopTargetProvider().TryResolve(new(new("linux-x64-gnu"), new(sysRoot: root)), out NativeTargetDescriptor? selected));
            NativeModule first = (await ParseSourceAsync("#include <only_selected.h>\nselected_value read_value();", selected, SourceLanguage.C)).module;
            NativeModule next = (await ParseSourceAsync("#include <only_selected.h>", Resolve("windows-x64-msvc"), SourceLanguage.C)).module;
            Assert.DoesNotContain(first.diagnostics, value => value.isFailure); Assert.Contains(next.diagnostics, value => value.isFailure);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public void RetargetingExplicitCppHeadersCannotLeakMutableState()
    {
        NativeTargetDescriptor original = new TargetResolver().Resolve(new(new("linux-x64-gnu"), new(cxxSystemIncludeFolders: [Path.GetTempPath()])));
        NativeTargetDescriptor other = Resolve("emscripten-wasm32-emscripten");
        Assert.Contains(Path.GetTempPath(), original.toolchain.cxxSystemIncludeFolders);
        Assert.DoesNotContain(Path.GetTempPath(), other.toolchain.cxxSystemIncludeFolders);
        Assert.Empty(other.toolchain.arguments);
    }
    [Theory]
    [InlineData("c++17")]
    [InlineData("c++23")]
    public async Task HostCppParsesTheSelectedStandardLibrary(string standard)
    {
        Assert.True(new DesktopTargetProvider().TryResolve(new(new("host")) { language = SourceLanguage.Cpp }, out NativeTargetDescriptor? target));
        NativeModule module = (await ParseSourceAsync("#include <cstddef>\n#include <cstdio>\n#include <string>\n#include <vector>\nstruct NativeVectorHolder { std::vector<std::string> values; std::size_t size; FILE* file; };", target, standard: standard)).module;
        Assert.DoesNotContain(module.diagnostics, value => value.isFailure); Assert.NotEmpty(target!.headerSearchPaths);
        Assert.Contains(module.declarations, value => value.name == "NativeVectorHolder");
    }
    [Fact]
    public void HostMatchesTheGeneratorProcessArchitecture()
    {
        NativeTargetDescriptor target = Resolve("host");
        string expected = RuntimeInformation.ProcessArchitecture switch { Architecture.X86 => "x86", Architecture.X64 => "x64", Architecture.Arm => "arm", Architecture.Arm64 => "arm64", _ => throw new PlatformNotSupportedException() };
        Assert.Equal(expected, target.architectureId); Assert.NotEqual("host", target.targetId.value);
    }
    [Fact]
    public void DeclaredToolchainIdentityDoesNotExecuteTheCompiler()
    {
        NativeTargetDescriptor original = new TargetResolver().Resolve(new(new("host"), new(compilerPath: "uninstalled-driver", compilerVersion: "declared-version")));
        NativeTargetDescriptor actual = ToolchainResolver.ResolveToolchain(original, SourceLanguage.Cpp);
        Assert.Equal(original.triple, actual.triple); Assert.Equal(original.toolchain.compilerPath, actual.toolchain.compilerPath); Assert.Equal("declared-version", actual.toolchain.compilerVersion);
    }
    [Theory]
    [InlineData("windows-x64-msvc", 4, 2, 8)]
    [InlineData("linux-x64-gnu", 8, 4, 16)]
    [InlineData("linux-arm64-gnu", 8, 4, 16)]
    [InlineData("macos-arm64-darwin", 8, 4, 8)]
    [InlineData("emscripten-wasm32-emscripten", 4, 4, 16)]
    public async Task PrimitivesUseTargetAbiSizes(
        string id,
        int longSize,
        int wcharSize,
        int longDoubleSize
    ) {
        NativeModule module = (await ParseSourceAsync("struct NativeAbiValues { char plainChar; long signedLong; unsigned long unsignedLong; wchar_t wide; long double extendedValue; };", Resolve(id))).module;
        Assert.DoesNotContain(module.diagnostics, value => value.isFailure);
        foreach ((string name, int size) in new[] { ("signedLong", longSize), ("unsignedLong", longSize), ("wide", wcharSize), ("extendedValue", longDoubleSize) })
            Assert.Equal(size, module.GetType(module.declarations.Single(value => value.name == name).type!.Value).size);
    }
    private static NativeTargetDescriptor Resolve(string id)
    {
        INativeTargetProvider provider = id.StartsWith("emscripten", StringComparison.Ordinal) ? new BrowserWasmTargetProvider() : new DesktopTargetProvider();
        return provider.TryResolve(new(new(id)), out NativeTargetDescriptor? target) ? target : throw new ArgumentException("Unowned fixture target: " + id);
    }
}
