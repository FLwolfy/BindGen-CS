using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeFunctionPointerTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task TypedCppCallbackPointersPreserveSignaturesAcrossBridgeReparseAndManagedInvocation(BindingImportMode importMode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-cpp-function-pointers 验收 & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "callbacks.hpp"), """
                namespace Sample {
                using Callback = void (*)(void*, const float*);
                inline int Invoke(Callback callback, void* context) {
                    const float values[] = {1.5f, 2.5f};
                    callback(context, values);
                    return 42;
                }
                inline Callback Round(Callback callback) { return callback; }
                inline void NativeCallback(void* context, const float* values) {
                    *static_cast<int*>(context) = static_cast<int>(values[0] + values[1]) + 10;
                }
                inline Callback Get() { return &NativeCallback; }
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "callbacks", nativeName = "callback_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["callbacks.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.Callbacks", name = "CallbackApi", importMode = importMode },
                runtime = new() { mode = "source" },  cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            var target = Assert.Single(result.targetModels);
            BridgeType signature = Assert.Single(target.bridge!.types, type => type.kind == BridgeTypeKind.Function);
            Assert.Equal(2, signature.parameterTypes.Length);
            Assert.Equal("C", signature.callingConvention);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            object? answer = await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, "callback_bridge", """
                using System.Runtime.CompilerServices;
                using System.Runtime.InteropServices;
                using Generated.Callbacks;
                public static unsafe class CallbackConsumer {
                    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
                    private static void Compute(void* context, float* values) {
                        *(int*)context = (int)(values[0] + values[1]);
                    }
                    public static int Run(string binary) {
                        using var api = API_FACTORY;
                        delegate* unmanaged[Cdecl]<void*, float*, void> callback = &Compute;
                        int observed = 0, result = 0;
                        if (api.Raw.Invoke(callback, &observed, &result, null) != 0 || result != 42 || observed != 4) return 1;
                        delegate* unmanaged[Cdecl]<void*, float*, void> round = null;
                        if (api.Raw.Round(callback, &round, null) != 0 || (nuint)round != (nuint)callback) return 2;
                        delegate* unmanaged[Cdecl]<void*, float*, void> native = null;
                        if (api.Raw.Get(&native, null) != 0 || native == null) return 3;
                        float* values = stackalloc float[2] {2, 3};
                        native(&observed, values);
                        return observed == 15 ? 42 : 4;
                    }
                }
                """.Replace("API_FACTORY", importMode == BindingImportMode.FunctionTable ? "CallbackApi.Load(binary)" : "CallbackApi.Create()", StringComparison.Ordinal),
                "CallbackConsumer", root, sourceRuntime: true);
            Assert.Equal(42, answer);
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

    [Theory]
    [InlineData("bool (*)(int)")]
    [InlineData("void (*)(bool)")]
    [InlineData("void (*)(Payload)")]
    public async Task CallbackValuesThatRequireConversionsCannotUseAFunctionPointerCast(string callback)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-cpp-unsafe-callback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "callbacks.hpp"), "struct Payload { int value; };\nusing Callback = " + callback + ";\ninline void Set(Callback value) { (void)value; }\n");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "callbacks", nativeName = "callback_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["callbacks.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult result = await host.GenerateAsync(new(configuration, root));
            Assert.False(result.success);
            Assert.Contains("typed callback conversion adapter", result.GetDiagnosticSummary(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(result.outputRoot));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
