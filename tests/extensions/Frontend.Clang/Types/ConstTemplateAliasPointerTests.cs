using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Frontend.Clang;
using BGCS.Infrastructure.Execution;
using BGCS.Targets.Desktop.Targeting;
using BGCS.Targets.Desktop.Toolchains;
using Xunit;
using Xunit.Abstractions;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ConstTemplateAliasPointerTests(ITestOutputHelper output) : FrontendTestHost
{
    [Fact]
    public async Task InstantiatedSdkPointerAliasesPreserveCanonicalPointeeQualifiers()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-const-template-alias-" + Guid.NewGuid().ToString("N"));
        string sdk = Directory.CreateDirectory(Path.Combine(root, "sdk")).FullName;
        try
        {
            string sdkHeader = Path.Combine(sdk, "view.hpp"), header = Path.Combine(root, "entry.hpp");
            await File.WriteAllTextAsync(sdkHeader, """
                #pragma once
                namespace SDK {
                template<class T> struct View {
                    using element_type = T;
                    using pointer = element_type*;
                    pointer data;
                };
                }
                """);
            const string declarations = """
                static_assert(sizeof(SDK::View<const int>) > 0);
                static_assert(sizeof(SDK::View<const char8_t>) > 0);
                static_assert(sizeof(SDK::View<char8_t>) > 0);
                static_assert(sizeof(SDK::View<volatile char8_t>) > 0);
                static_assert(sizeof(SDK::View<const volatile char8_t>) > 0);
                using IntPointer = SDK::View<const int>::pointer;
                using Utf8Pointer = SDK::View<const char8_t>::pointer;
                using MutableUtf8Pointer = SDK::View<char8_t>::pointer;
                using VolatileUtf8Pointer = SDK::View<volatile char8_t>::pointer;
                using ConstVolatileUtf8Pointer = SDK::View<const volatile char8_t>::pointer;
                IntPointer read_int();
                Utf8Pointer read_utf8();
                MutableUtf8Pointer read_mutable_utf8();
                VolatileUtf8Pointer read_volatile_utf8();
                ConstVolatileUtf8Pointer read_const_volatile_utf8();
                """;
            await File.WriteAllTextAsync(header, "#include <view.hpp>\n" + declarations);
            string compiler = FindClang();
            var target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"), new(compilerPath: compiler))), SourceLanguage.Cpp);
            FrontendRequest request = new(target, SourceLanguage.Cpp, [header])
            {
                pathBase = root,
                languageStandard = "c++23",
                systemIncludeDirectories = [sdk],
                parseMacros = false,
                parseSystemIncludes = false,
            };
            NativeModule module = (await new ClangFrontend(host).ParseAsync(request, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);

            // Establish the native expectation independently of detached aliases.
            string proof = Path.Combine(root, "native-proof.cpp");
            await File.WriteAllTextAsync(proof, "#include <view.hpp>\n" + declarations + """

                static_assert(__is_same(IntPointer, const int*));
                static_assert(__is_same(Utf8Pointer, const char8_t*));
                static_assert(!__is_same(Utf8Pointer, char8_t*));
                static_assert(__is_same(MutableUtf8Pointer, char8_t*));
                static_assert(__is_same(VolatileUtf8Pointer, volatile char8_t*));
                static_assert(__is_same(ConstVolatileUtf8Pointer, const volatile char8_t*));
                """);
            string[] arguments = ["--no-default-config", "--target=" + target.triple, "-std=c++23", "-fsyntax-only", "-Werror", proof,
                .. target.toolchain.arguments,
                .. (target.toolchain.sysRoot is string sysroot ? new[] { "--sysroot=" + sysroot } : []), "-isystem", sdk,
                .. target.toolchain.systemIncludeFolders.Concat(target.toolchain.cxxSystemIncludeFolders).SelectMany(directory => new[] { "-isystem", directory })];
            var native = await ProcessExecutor.ExecuteAsync(target.toolchain.compilerPath!, arguments, root, TimeSpan.FromSeconds(60));
            Assert.False(native.timedOut);
            Assert.True(native.exitCode == 0, native.standardOutput + native.standardError);

            (string functionName, string primitive, string[] qualifiers)[] expectations =
            [
                ("read_int", "Int", ["Const"]),
                ("read_utf8", "Char8", ["Const"]),
                ("read_mutable_utf8", "Char8", []),
                ("read_volatile_utf8", "Char8", ["Volatile"]),
                ("read_const_volatile_utf8", "Char8", ["Const", "Volatile"])
            ];
            foreach ((string functionName, string primitive, string[] qualifiers) in expectations)
            {
                NativeDeclaration function = Assert.Single(module.declarations, declaration => declaration.qualifiedName == functionName);
                string description = Describe(function.returnType!.Value);
                output.WriteLine(functionName + ": " + description);
                NativeType pointer = Unalias(function.returnType!.Value);
                Assert.Equal(NativeTypeCategory.Pointer, pointer.category);
                NativeType pointee = Unalias(pointer.elementType!.Value);
                foreach (string qualifier in qualifiers)
                {
                    Assert.True(pointee.category == NativeTypeCategory.Qualified, functionName + " lost canonical pointee " + qualifier + ": " + description);
                    Assert.Equal(qualifier, pointee.qualifier);
                    pointee = Unalias(pointee.elementType!.Value);
                }
                Assert.Equal(NativeTypeCategory.Primitive, pointee.category);
                Assert.Equal(primitive, pointee.primitiveKind);
                Assert.Equal(primitive == "Int", pointee.isSigned);
                Assert.Equal(primitive == "Int" ? 4 : 1, pointee.size);
                Assert.Equal(primitive == "Int" ? "int" : "char8_t", pointee.spelling);
            }

            NativeType Unalias(StableId identity)
            {
                HashSet<StableId> visited = [];
                NativeType type = module.GetType(identity);
                while (type.category == NativeTypeCategory.Alias)
                {
                    Assert.True(visited.Add(type.id));
                    type = module.GetType(type.elementType!.Value);
                }
                return type;
            }
            string Describe(StableId identity)
            {
                List<NativeType> chain = [];
                HashSet<StableId> visited = [];
                while (visited.Add(identity))
                {
                    NativeType type = module.GetType(identity);
                    chain.Add(type);
                    if (type.elementType is not StableId element) break;
                    identity = element;
                }
                return JsonSerializer.Serialize(chain.Select(type => new { type.category, type.spelling, type.qualifier, type.primitiveKind }));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string FindClang()
    {
        string installed = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin", "clang++.exe")
            : OperatingSystem.IsMacOS() ? "/usr/bin/clang++" : "/usr/bin/clang++-20";
        if (File.Exists(installed)) return installed;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (string name in OperatingSystem.IsWindows() ? new[] { "clang++.exe", "clang.exe" } : new[] { "clang++-20", "clang++", "clang-20", "clang" })
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        throw new FileNotFoundException("The independent native qualifier oracle requires an installed Clang driver; this is a test prerequisite, not a generator discovery service.");
    }
}
