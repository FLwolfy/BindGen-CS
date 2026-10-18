using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.Workers;
using BGCS.Cpp2C.Emission;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeSdkStorageAccessibilityTests
{
    [Fact]
    public async Task RequiredSdkStoragePreservesAccessAndProvesOnlyNameableTypesWithoutLosingPrivateFacts()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-sdk-storage-access-" + Guid.NewGuid().ToString("N"));
        string sdk = Path.Combine(root, "sdk");
        Directory.CreateDirectory(sdk);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(sdk, "storage.hpp"), """
                namespace SDK {
                template<class T> struct Holder { T value; };
                template<class T> class Storage {
                    struct PrivateRep { T value; };
                    using PrivateAlias = T;
                    Holder<PrivateRep> hidden;
                    Holder<const PrivateRep*> hidden_pointer;
                protected:
                    struct ProtectedRep { T value; };
                    Holder<ProtectedRep> protected_storage;
                public:
                    struct PublicRep {
                #if defined(BGCS_PUBLIC_FLOAT)
                        float value;
                #else
                        T value;
                #endif
                    };
                    int code;
                    PrivateAlias public_alias;
                    PublicRep public_storage;
                    PrivateRep exposed_private_type;
                };
                int unrelated_sdk_function();
                }
                """);
            string header = Path.Combine(root, "entry.hpp");
            await File.WriteAllTextAsync(header, """
                #include <storage.hpp>
                static_assert(sizeof(SDK::Storage<int>) > 0);
                SDK::Storage<int>* read_storage();
                SDK::Storage<int>::PublicRep* read_public();
                struct UserStorage {
                    int visible;
                private:
                #if defined(BGCS_PRIVATE_FLOAT)
                    float private_value;
                #else
                    int private_value;
                #endif
                };
                UserStorage* read_user();
                """);
            var target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp);
            FrontendRequest request = new(target, SourceLanguage.Cpp, [header])
            {
                pathBase = root, languageStandard = "c++23", systemIncludeDirectories = [sdk], parseMacros = false, parseSystemIncludes = false
            };
            FrontendWorkerClient worker = new(Path.Combine(WorkerRoot(), "BGCS.Frontend.Clang.Worker.dll"));
            NativeModule module = (await worker.ParseAsync(request, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration privateRep = Record("PrivateRep"), protectedRep = Record("ProtectedRep"), publicRep = Record("PublicRep");
            Assert.Equal("Private", privateRep.visibility);
            Assert.Equal("Protected", protectedRep.visibility);
            Assert.Equal("Public", publicRep.visibility);
            Assert.True(privateRep.isSystemDeclaration);
            Assert.True(publicRep.isSystemDeclaration);
            NativeDeclaration privateField = Assert.Single(privateRep.children.Select(module.GetDeclaration), declaration => declaration.kind == "Field");
            Assert.Equal("value", privateField.name);
            Assert.Equal(0, privateField.offset);
            Assert.Equal("Int", module.GetType(privateField.type!.Value).primitiveKind);
            Assert.Contains(module.declarations, declaration => declaration.templateArguments.Any(argument => argument.type is { } id
                && module.GetType(id).declaration == privateRep.id));
            Assert.DoesNotContain(module.declarations, declaration => declaration.name == "unrelated_sdk_function");
            Assert.False(Record("UserStorage").isStandardLayout);

            string proof = new NativeStorageAssertionEmitter().Emit(module, [header]);
            Assert.Contains("sizeof(" + publicRep.qualifiedName + ")", proof);
            Assert.Contains("alignof(" + publicRep.qualifiedName + ")", proof);
            Assert.Contains("decltype(((" + publicRep.qualifiedName + "*)nullptr)->value)", proof);
            Assert.Contains("->public_alias)", proof);
            Assert.Contains("->exposed_private_type)", proof);
            Assert.DoesNotContain("PrivateRep", proof);
            Assert.DoesNotContain("ProtectedRep", proof);
            string input = Path.Combine(root, "proof.cpp");
            await File.WriteAllTextAsync(input, proof);
            ImmutableArray<string> arguments = OperatingSystem.IsMacOS()
                ? ["-std=c++23", "-fsyntax-only", input, "-isystem", sdk,
                    .. NativeConsumerHarness.CompilerSdkArguments(SourceLanguage.Cpp, target)]
                : ["-std=c++23", "-fsyntax-only", input,
                .. target.toolchain.arguments,
                .. (target.toolchain.sysRoot is string sysroot ? new[] { "--sysroot=" + sysroot } : []),
                "-isystem", sdk,
                .. target.toolchain.cxxSystemIncludeFolders.Concat(target.toolchain.systemIncludeFolders).SelectMany(directory => new[] { "-isystem", directory })];
            var compiled = await ProcessExecutor.ExecuteAsync(NativeConsumerHarness.FindCompiler(true), arguments, root, TimeSpan.FromSeconds(60));
            Assert.False(compiled.timedOut);
            Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
            var publicDrift = await ProcessExecutor.ExecuteAsync(NativeConsumerHarness.FindCompiler(true), arguments.Add("-DBGCS_PUBLIC_FLOAT=1"), root, TimeSpan.FromSeconds(60));
            Assert.False(publicDrift.timedOut);
            Assert.NotEqual(0, publicDrift.exitCode);
            Assert.Contains("BGCS native field type differs", publicDrift.standardOutput + publicDrift.standardError);

            NativeModule changed = (await worker.ParseAsync(request with { defines = ["BGCS_PRIVATE_FLOAT=1"] }, CancellationToken.None)).module;
            Assert.DoesNotContain(changed.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration originalPrivate = Assert.Single(module.declarations, declaration => declaration.qualifiedName == "UserStorage::private_value");
            NativeDeclaration changedPrivate = Assert.Single(changed.declarations, declaration => declaration.qualifiedName == "UserStorage::private_value");
            Assert.Equal("Int", module.GetType(originalPrivate.type!.Value).primitiveKind);
            Assert.Equal("Float", changed.GetType(changedPrivate.type!.Value).primitiveKind);

            NativeDeclaration Record(string name) => Assert.Single(module.declarations, declaration => declaration.kind == "Class"
                && declaration.name == name && declaration.isCompleteDefinition);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
