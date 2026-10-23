using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCompilerIntrinsicTests
{
    [Fact]
    public async Task MsvcDirectCallIntrinsicsBecomeNativeExportsAndSignedCharSignaturesRetainTheirExactTypes()
    {
        if (!OperatingSystem.IsWindows()) return; // This receipt certifies the actual Windows MSVC provider only.
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-intrinsic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "intrinsics.h"), """
                #pragma once
                void _ReadWriteBarrier(void);
                void __debugbreak(void);
                #pragma intrinsic(_ReadWriteBarrier)
                #pragma intrinsic(__debugbreak)
                typedef signed char SignedByte;
                __declspec(dllexport) SignedByte EchoSigned(SignedByte value);
                __declspec(dllexport) void ReadSigned(SignedByte* value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "intrinsics.c"), """
                #include "intrinsics.h"
                SignedByte EchoSigned(SignedByte value) { return value; }
                void ReadSigned(SignedByte* value) { *value = -43; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "intrinsics", nativeName = "intrinsic_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["intrinsics.h"], parseMacros = false, includeHeaderHelpers = true },
                api = new() { @namespace = "Generated.Intrinsics", name = "IntrinsicApi" },

                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            foreach (string name in new[] { "_ReadWriteBarrier", "__debugbreak" })
            {
                NativeDeclaration native = Assert.Single(target.native.declarations, declaration => declaration.kind == "Function" && declaration.name == name);
                Assert.Equal(NativeCallableAddressability.DirectCallOnly, native.addressability);
                Assert.StartsWith(target.bridge!.exportPrefix + "_", Assert.Single(target.interop.functions, function => function.sourceName == name).exportName);
            }
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            object? answer = await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, "intrinsic_fixture", """
                using Generated.Intrinsics;
                public static unsafe class IntrinsicConsumer {
                    public static int Run(string binary) {
                        using var api = IntrinsicApi.Load(binary);
                        api.Raw.ReadWriteBarrier();
                        if (api.Raw.EchoSigned(-41) != -41) return 1;
                        sbyte value = 0;
                        api.Raw.ReadSigned(&value);
                        return value == -43 ? 42 : 2;
                    }
                }
                """, "IntrinsicConsumer", root, sourceRuntime: false);
            Assert.Equal(42, answer);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
