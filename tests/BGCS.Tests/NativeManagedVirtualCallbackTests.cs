using BGCS.Core.Targets;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Bridge;
using BGCS.Core.Capabilities;
using BGCS.Core.Inputs;
using BGCS.Core.Managed;
using BGCS.Core.Artifacts;
using BGCS.Core.Interop;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeManagedVirtualCallbackTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task GeneratedStaticThunksRetainIndependentTargetsAndCloseOnlyAfterNativeQuiescence(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-managed-virtual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "calculator.hpp"), """
                #ifndef BGCS_VIRTUAL_USER_PREAMBLE
                #error BGCS_VIRTUAL_USER_PREAMBLE_MISSING
                #endif
                namespace Demo {
                inline int live = 0;
                class Calculator {
                public:
                    Calculator() { ++live; }
                    virtual ~Calculator() noexcept { --live; }
                    virtual int Compute(int value) const = 0;
                    virtual void Notify(int& value) volatile = 0;
                    virtual bool Accept(bool value) const = 0;
                    virtual void* Echo(void* value) const = 0;
                };
                inline int Call(Calculator* target, int value) { return target->Compute(value); }
                inline int NotifyCall(Calculator* target, int value) { target->Notify(value); return value; }
                inline bool BoolCall(Calculator* target, bool value) { return target->Accept(value); }
                inline void* EchoCall(Calculator* target, void* value) { return target->Echo(value); }
                inline int Live() { return live; }
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "managed_callbacks", nativeName = "managed_callbacks", bridgeExportPrefix = "managed_callbacks" },
                source = new() { language = SourceLanguage.Cpp, headers = ["calculator.hpp"], preamble = "#define BGCS_VIRTUAL_USER_PREAMBLE 1\n", parseMacros = false },
                bridge = new() { cpp = new() { virtualCallbacks = [new("calculator", "Demo::Calculator") { releaseThreadDomain = "any" }] } },
                api = new()
                {
                    @namespace = "Generated.Virtual",
                    name = "VirtualApi",
                    importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    names = ImmutableDictionary<string, string>.Empty.Add("managed_callbacks_error", "CallbackError")
                        .Add("managed_callbacks_error_release", "ReleaseError")
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string targetRoot = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string factInput = await File.ReadAllTextAsync(Path.Combine(targetRoot, "native", "src", "storage.assertions.cpp"));
            int userPreamble = factInput.IndexOf("#define BGCS_VIRTUAL_USER_PREAMBLE 1", StringComparison.Ordinal);
            Assert.True(userPreamble >= 0, factInput);
            int support = factInput.IndexOf(BGCS.NativeBridge.Planning.VirtualCallbackPlanner.preamble, userPreamble, StringComparison.Ordinal);
            Assert.True(support > userPreamble, factInput);
            int userHeader = factInput.IndexOf("calculator.hpp", support, StringComparison.Ordinal);
            Assert.True(userHeader > support, factInput);
            Assert.DoesNotContain("#include", factInput[(userHeader + "calculator.hpp".Length)..]);
            string bridgeSource = await File.ReadAllTextAsync(Path.Combine(targetRoot, "native", "src", "managed_callbacks.cpp"));
            Assert.Contains("namespace BGCSGeneratedCallbacks", bridgeSource);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(targetRoot, "metadata", "abi.manifest.json")));
            Assert.Single(manifest.RootElement.GetProperty("managedVirtualProxies").EnumerateArray());
            Assert.Empty(manifest.RootElement.GetProperty("managedVirtualProxyOmissions").EnumerateArray());
            InteropModule finalAbi = manifest.RootElement.GetProperty("module").Deserialize<InteropModule>()!;
            VirtualProxyContract generatedProxy = Assert.Single(finalAbi.virtualProxies);
            var sourceInterface = Assert.Single(result.targetModels).native.GetType(generatedProxy.sourceInterface);
            BindingCapabilityReport capability = Assert.Single(result.capabilities, value => value.declarationId == sourceInterface.declaration);
            Assert.Equal(DeclarationGenerationStatus.GeneratedFriendly, capability.status);
            ManagedApiModel managed = new DefaultManagedProjection().Project(finalAbi,
                new(configuration.api.@namespace, configuration.api.name) { style = configuration.api.style, names = configuration.api.names }, CancellationToken.None);
            CSharpEmissionPlan emission = new HostTestEmissionPlanner().Plan(managed, new(configuration.library.nativeName, new NoWrites())
            { importMode = mode, runtimeNamespace = sourceRuntime ? "Generated.Virtual.Runtime" : "BGCS.Runtime", includeRuntimeSource = sourceRuntime });
            CSharpEmissionPlan nonblocking = new HostTestEmissionPlanner().Plan(managed, new(configuration.library.nativeName, new NoWrites())
            {
                importMode = mode,
                runtimeNamespace = sourceRuntime ? "Generated.Virtual.Runtime" : "BGCS.Runtime",
                managedRuntime = new("fixture-explicit-no-block", new BGCS.Core.Targets.ManagedRuntimeId("dotnet.coreclr"),
                    BGCS.Core.Targets.NativeModuleMechanism.DynamicLibrary, [mode], true, false)
            });
            Assert.Empty(nonblocking.virtualProxies);
            Assert.Contains("nonblocking virtual-proxy protocol", Assert.Single(nonblocking.virtualProxyOmissions).reason, StringComparison.Ordinal);
            Assert.Equal(emission.methods.Select(method => method.nativeExport), nonblocking.methods.Select(method => method.nativeExport));
            var actualSources = Directory.GetFiles(Path.Combine(targetRoot, "managed"), "*.cs", SearchOption.AllDirectories)
                .Select(path => (Path: path, Text: File.ReadAllText(path))).ToArray();
            new EmittedInteropContractReader().Verify(emission, actualSources, configuration.library.nativeName);
            foreach ((string before, string after) in new[]
            {
                ("CallbackShutdownOrder.KeepOpenUntilUnregistered", "CallbackShutdownOrder.CloseBeforeUnregister"),
                ("TryEnterInvocation(out invocation)", "TryEnterInvocation(out invocation) && false"),
                ("status = 0;", "status = 1;")
            })
            {
                Assert.Contains(actualSources, source => source.Text.Contains(before, StringComparison.Ordinal));
                var changed = actualSources.Select(source => (source.Path, source.Text.Replace(before, after, StringComparison.Ordinal)));
                Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(emission, changed, configuration.library.nativeName));
            }
            string contextName = sourceRuntime ? "VirtualApiContext" : "VirtualApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string staticInitialize = mode == BindingImportMode.FunctionTable ? "VirtualApi.Load(binary);" : "VirtualApi.Create();";
            string staticCheck = sourceRuntime ? $$"""
                {{staticInitialize}}
                using (var shared = VirtualApi.CreateCalculatorCallbacks(new Target(11), static _ => { })) {
                    if (shared.Invoke(pointer => { int value = 0; CallbackError* error = null;
                        if (VirtualApi.Raw.Call(pointer, 31, &value, &error) != 0) throw new Exception(); return value; }) != 42) return 20;
                }
                if (VirtualApi.WithCalculatorCallbacks(new Target(12), pointer => 42) != 42) return 21;
                VirtualApi.Dispose();
                """ : "";
            string consumer = $$"""
                using System;
                using System.Runtime.InteropServices;
                using Generated.Virtual;
                using ApiContext = Generated.Virtual.{{contextName}};
                public static unsafe class ManagedVirtualConsumer {
                    private sealed class Target(int offset) : ApiContext.ICalculatorCallbacks {
                        public Action? onCompute;
                        public int Compute(int value) { onCompute?.Invoke(); if (value < 0) throw new InvalidOperationException("managed callback failure"); return value + offset; }
                        public void Notify(nint value) { Marshal.WriteInt32(value, Marshal.ReadInt32(value) + offset); }
                        public bool Accept(bool value) => !value;
                        public nint Echo(nint value) => value;
                    }
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        using var observer = {{factory}};
                        int stops = 0;
                        Target target = new(10);
                        using var registration = api.CreateCalculatorCallbacks(target, _ => { stops++; });
                        if (observer.Live() != 1) return 1;
                        int value = registration.Invoke(pointer => { int output = 0; CallbackError* error = null;
                            if (api.Raw.Call(pointer, 32, &output, &error) != 0 || error != null) throw new Exception(); return output; });
                        if (value != 42) return 2;
                        if (registration.Invoke(pointer => { int output = 0; CallbackError* error = null;
                            if (api.Raw.NotifyCall(pointer, 32, &output, &error) != 0 || error != null) throw new Exception(); return output; }) != 42) return 3;
                        if (registration.Invoke(pointer => { byte output = 0; CallbackError* error = null;
                            if (api.Raw.BoolCall(pointer, 0, &output, &error) != 0 || error != null) throw new Exception(); return output; }) != 1) return 4;
                        if (registration.Invoke(pointer => { void* output = null; CallbackError* error = null;
                            if (api.Raw.EchoCall(pointer, (void*)42, &output, &error) != 0 || error != null) throw new Exception(); return (nint)output; }) != 42) return 5;
                        registration.Invoke(pointer => { int output = 0; CallbackError* error = null;
                            int status = api.Raw.Call(pointer, -1, &output, &error);
                            if (status == 0 || error == null || output != 0) throw new Exception(); api.Raw.ReleaseError(error); return 0; });
                        if (registration.TakeCallbackException()?.Message != "managed callback failure" || registration.TakeCallbackException() != null) return 6;
                        int nested = api.WithCalculatorCallbacks(new Target(20), pointer => { int output = 0; CallbackError* error = null;
                            if (api.Raw.Call(pointer, 22, &output, &error) != 0 || error != null) throw new Exception(); return output; });
                        if (nested != 42 || observer.Live() != 1) return 7;
                        Target selfTarget = new(30);
                        ApiContext.CalculatorCallbackRegistration? self = null;
                        selfTarget.onCompute = () => self!.Dispose();
                        self = api.CreateCalculatorCallbacks(selfTarget, static _ => { });
                        if (self.Invoke(pointer => { int output = 0; CallbackError* error = null;
                            if (api.Raw.Call(pointer, 12, &output, &error) != 0 || error != null) throw new Exception(); return output; }) != 42) return 8;
                        self.completion.GetAwaiter().GetResult();
                        if (observer.Live() != 1 || self.closeFailure is not null) return 9;
                        api.Dispose();
                        try { registration.Invoke(pointer => 1); return 10; } catch (ObjectDisposedException) { }
                        var drain = api.DisposeAsync();
                        if (drain.IsCompleted) return 11;
                        registration.Dispose();
                        drain.GetAwaiter().GetResult();
                        if (stops != 1 || observer.Live() != 0 || registration.closeFailure is not null) return 12;
                        {{staticCheck}}
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary,
                configuration.library.nativeName, consumer, "ManagedVirtualConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }

    private sealed class NoWrites : IArtifactSink
    {
        public string WriteText(
            string relativePath,
            string content
        ) => throw new InvalidOperationException("Reconstructing a frozen plan must not emit or publish files.");
    }
}
