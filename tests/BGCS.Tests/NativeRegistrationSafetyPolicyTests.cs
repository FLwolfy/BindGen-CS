using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeRegistrationSafetyPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactRegistrationCleanupPassesStrictSafetyWhileIncompleteProtocolsPreserveThePreviousPublication(bool asynchronous)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-registration-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "registration.h"), """
                #pragma once
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                #ifdef __cplusplus
                extern "C" {
                #endif
                typedef struct Registration Registration;
                typedef void (*Completion)(int result, int status, void* state);
                API Registration* Start(Completion callback, void* state);
                API void StopAndJoin(Registration* handle);
                API void Pump(void);
                API int LiveCount(void);
                API int JoinedCount(void);
                API int TailCount(void);
                #ifdef __cplusplus
                }
                #endif
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "registration.cpp"), """
                #include "registration.h"
                #include <atomic>
                #include <condition_variable>
                #include <mutex>
                #include <new>
                struct Registration {
                    Completion callback; void* state; std::mutex gate;
                    std::condition_variable changed; int active = 0;
                };
                static std::mutex registry_gate;
                static Registration* current = nullptr;
                static std::atomic<int> live{0}, joined{0}, tails{0};
                API Registration* Start(Completion callback, void* state) {
                    if (!callback) return nullptr;
                    std::lock_guard<std::mutex> registry(registry_gate);
                    if (current) return nullptr;
                    current = new(std::nothrow) Registration{callback, state};
                    if (current) ++live;
                    return current;
                }
                API void Pump(void) {
                    Registration* handle;
                    {
                        std::lock_guard<std::mutex> registry(registry_gate);
                        handle = current;
                        if (!handle) return;
                        std::lock_guard<std::mutex> frame(handle->gate); ++handle->active;
                    }
                    handle->callback(42, 0, handle->state);
                    ++tails; // A completed managed callback is not the native-frame end.
                    {
                        std::lock_guard<std::mutex> frame(handle->gate);
                        --handle->active; handle->changed.notify_all();
                    }
                }
                API void StopAndJoin(Registration* handle) {
                    {
                        std::lock_guard<std::mutex> registry(registry_gate);
                        if (current == handle) current = nullptr;
                    }
                    {
                        std::unique_lock<std::mutex> frame(handle->gate);
                        handle->changed.wait(frame, [handle] { return handle->active == 0; });
                    }
                    delete handle; --live; ++joined;
                }
                API int LiveCount(void) { return live; }
                API int JoinedCount(void) { return joined; }
                API int TailCount(void) { return tails; }
                """);
            CallbackParameterConfiguration callback = new()
            {
                function = "Start", parameter = "callback", retained = true,
                lifetime = asynchronous ? NativeLifetimeKind.Operation : NativeLifetimeKind.Registration,
                threading = NativeCallbackThreading.AnyThread, userDataParameter = "state", callableUserDataIndex = 2,
                unregisterFunction = "StopAndJoin", unregisterQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations,
                registration = new() { unregisterHandleParameter = "handle", releaseThreadDomain = "any",
                    nullResultQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations }
            };
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "registration-safety", nativeName = "registration_safety_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["registration.h"], parseMacros = false,
                    includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.RegistrationSafety", name = "RegistrationApi" },
                semantics = new()
                {
                    callbackParameters = [callback],
                    asyncOperations = asynchronous ? [new()
                    {
                        function = "Start", completionCallback = "callback", resultArgumentIndex = 0, statusArgumentIndex = 1,
                        completionQuiescence = NativeQuiescenceKind.NoNewInvocations, successStatus = "0", retainedParameters = []
                    }] : []
                },
                nativeBuild = new() { mode = "sources", sources = ["registration.cpp"] },
                execution = new() { diagnostics = new() { warningsAsErrors = true } },
                cache = new() { enabled = false }
            };
            await using BindingHost host = Host();
            BuildResult built = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            Assert.DoesNotContain(built.generation.diagnostics, diagnostic => diagnostic.code == BindingDiagnosticCodes.C_OWNERSHIP);
            TargetInspection target = Assert.Single(built.generation.targetModels);
            InteropFunction start = Assert.Single(target.interop.functions, function => function.exportName == "Start");
            Assert.Null(start.returnOwnership);
            Assert.Equal(EvidenceLevel.UserAsserted, start.parameters[0].callback!.evidence.level);
            Assert.Equal(EvidenceLevel.UserAsserted, start.parameters[0].callback!.registration!.evidence.level);
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin")));
            string body = asynchronous ? """
                using (var operation = api.StartAsync()) {
                    if (api.LiveCount() != 1 || operation.task.IsCompleted) return 1;
                    api.Raw.Pump();
                    if (operation.task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() != 42) return 2;
                    if (api.LiveCount() != 0 || api.JoinedCount() != 1 || api.TailCount() != 1) return 3;
                }
                """ : """
                int observed = 0;
                using (var registration = api.Start((value, status) => observed = value + status)) {
                    if (api.LiveCount() != 1) return 1;
                    registration.Invoke(handle => { api.Raw.Pump(); return 0; });
                    if (observed != 42 || api.LiveCount() != 1 || api.TailCount() != 1 || api.JoinedCount() != 0) return 2;
                    registration.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    if (!registration.completion.IsCompletedSuccessfully || registration.closeFailure is not null) return 3;
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary,
                configuration.library.nativeName, $$"""
                using System;
                using Generated.RegistrationSafety;
                public static unsafe class StrictRegistrationConsumer {
                    public static int Run(string binary) {
                        using var api = RegistrationApi.Load(binary);
                        {{body}}
                        return api.LiveCount() == 0 && api.JoinedCount() == 1 && api.TailCount() == 1 ? 42 : 4;
                    }
                }
                """, "StrictRegistrationConsumer", root, sourceRuntime: false));

            Dictionary<string, string> previous = Snapshot(built.outputRoot);
            foreach (string fault in asynchronous
                ? new[] { "no-full-join", "null-failure", "missing-unregister", "unknown-completion" }
                : new[] { "no-full-join", "null-failure", "missing-unregister" })
            {
                CallbackParameterConfiguration incomplete = fault switch
                {
                    "no-full-join" => callback with { unregisterQuiescence = NativeQuiescenceKind.NoNewInvocations },
                    "null-failure" => callback with { registration = callback.registration! with { nullResultQuiescence = NativeQuiescenceKind.Unknown } },
                    "missing-unregister" => callback with { registration = null, unregisterFunction = null },
                    _ => callback
                };
                BindingConfiguration request = configuration with { semantics = configuration.semantics with
                {
                    callbackParameters = [incomplete],
                    asyncOperations = fault == "unknown-completion" ? [configuration.semantics.asyncOperations[0] with
                        { completionQuiescence = NativeQuiescenceKind.Unknown }] : configuration.semantics.asyncOperations
                } };
                GenerationResult rejected = await host.GenerateAsync(new(request, root));
                Assert.False(rejected.success);
                Assert.False(rejected.published);
                BindingDiagnostic failure = Assert.Single(rejected.diagnostics, diagnostic => diagnostic.code == BindingDiagnosticCodes.C_OWNERSHIP);
                Assert.Equal(BindingDiagnosticSeverity.Error, failure.severity);
                Assert.Contains("semantics.callbackParameters", failure.message, StringComparison.Ordinal);
                Assert.DoesNotContain("Declare semantics.ownedResults", failure.message, StringComparison.Ordinal);
                Assert.NotNull(failure.location);
                Assert.EndsWith("registration.h", failure.location.Value.file, StringComparison.Ordinal);
                Dictionary<string, string> after = Snapshot(built.outputRoot);
                Assert.Equal(previous.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
                foreach ((string path, string digest) in previous) Assert.Equal(digest, after[path]);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static BindingHost Host()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(Path.Combine(root!.FullName,
            "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0")).Build();
    }
}
