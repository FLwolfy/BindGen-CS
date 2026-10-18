using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeRetainedCallbackTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task RealNativeThreadsRetainStateUntilFullJoinAndSelfCloseNeverJoinsItsOwnFrame(BindingImportMode mode, bool sourceRuntime)
        => await RunAsync(mode, sourceRuntime, paired: false);

    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task SameCallbackAndUserDataUnregisterWaitsForTheCompleteNativeThreadFrame(BindingImportMode mode, bool sourceRuntime)
        => await RunAsync(mode, sourceRuntime, paired: true);

    private static async Task RunAsync(BindingImportMode mode, bool sourceRuntime, bool paired)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-retained-callback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string identity = paired ? "#define CB_PAIR 1\n" : "#define CB_PAIR 0\n";
            await File.WriteAllTextAsync(Path.Combine(root, "retained.h"), identity + """
                #pragma once
                #include <stdbool.h>
                #if defined(_WIN32)
                #define CB_API __declspec(dllexport)
                #else
                #define CB_API __attribute__((visibility("default")))
                #endif
                #ifdef __cplusplus
                extern "C" {
                #endif
                typedef struct Registration Registration;
                typedef int (*RetainedHandler)(int value, void* state);
                #if CB_PAIR
                CB_API bool Register(RetainedHandler callback, int seed, void* state);
                CB_API int Trigger(void* state, int value);
                CB_API void StopAndJoin(RetainedHandler callback, void* state);
                #else
                CB_API Registration* Register(RetainedHandler callback, int seed, void* state);
                CB_API int Trigger(Registration* handle, int value);
                CB_API void StopAndJoin(Registration* handle);
                #endif
                CB_API int LiveCount(void);
                CB_API int LastResult(void);
                CB_API int IdentityFailures(void);
                #ifdef __cplusplus
                }
                #endif
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "retained.cpp"), """
                #include "retained.h"
                #include <atomic>
                #include <chrono>
                #include <condition_variable>
                #include <cstdlib>
                #include <mutex>
                #include <thread>
                #include <vector>
                static std::atomic<int> live_count{0}, last_result{0}, identity_failures{0};
                struct Registration {
                    RetainedHandler callback; void* state; std::mutex gate; std::condition_variable changed;
                    bool stopping=false, pending=false, done=false; int value=0, result=0; std::thread thread;
                    Registration(RetainedHandler callback, void* state) : callback(callback), state(state) {}
                };
                static Registration* Create(RetainedHandler callback, int seed, void* state) {
                    if (seed < 0) return nullptr;
                    callback(seed,state); // The managed state must exist before the returned handle is published.
                    Registration* handle = new Registration(callback,state); ++live_count;
                    handle->thread=std::thread([handle] {
                        std::unique_lock<std::mutex> lock(handle->gate);
                        for (;;) {
                            handle->changed.wait(lock,[&] { return handle->pending || handle->stopping; });
                            if(handle->stopping) return;
                            int value=handle->value; handle->pending=false; lock.unlock();
                            int result=handle->callback(value,handle->state);
                            std::this_thread::sleep_for(std::chrono::milliseconds(25)); // Native work continues after managed thunk finally.
                            last_result=result; lock.lock(); handle->result=result+1; handle->done=true; handle->changed.notify_all();
                        }
                    });
                    return handle;
                }
                static int Invoke(Registration* handle,int value) {
                    std::unique_lock<std::mutex> lock(handle->gate); handle->value=value; handle->done=false; handle->pending=true; handle->changed.notify_all();
                    handle->changed.wait(lock,[&] { return handle->done; }); return handle->result;
                }
                static void Destroy(Registration* handle) {
                    {std::lock_guard<std::mutex> lock(handle->gate); handle->stopping=true; handle->changed.notify_all();}
                    handle->thread.join(); delete handle; --live_count;
                }
                #if CB_PAIR
                static std::mutex registrations_gate;
                static std::vector<Registration*> registrations;
                CB_API bool Register(RetainedHandler callback, int seed, void* state) {
                    Registration* handle=Create(callback,seed,state); if(!handle) return false;
                    std::lock_guard<std::mutex> lock(registrations_gate); registrations.push_back(handle); return true;
                }
                CB_API int Trigger(void* state,int value) {
                    Registration* handle=nullptr;
                    {std::lock_guard<std::mutex> lock(registrations_gate); for(auto candidate:registrations) if(candidate->state==state) {handle=candidate;break;}}
                    if(!handle) std::abort(); return Invoke(handle,value);
                }
                CB_API void StopAndJoin(RetainedHandler callback,void* state) {
                    Registration* handle=nullptr;
                    {std::lock_guard<std::mutex> lock(registrations_gate);
                        for(auto item=registrations.begin();item!=registrations.end();++item)
                            if((*item)->state==state) {
                                handle=*item; if(handle->callback!=callback) ++identity_failures;
                                registrations.erase(item);break;}}
                    if(!handle) std::abort(); Destroy(handle);
                }
                #else
                CB_API Registration* Register(RetainedHandler callback,int seed,void* state) {return Create(callback,seed,state);}
                CB_API int Trigger(Registration* handle,int value) {return Invoke(handle,value);}
                CB_API void StopAndJoin(Registration* handle) {Destroy(handle);}
                #endif
                CB_API int LiveCount(void) { return live_count; }
                CB_API int LastResult(void) { return last_result; }
                CB_API int IdentityFailures(void) { return identity_failures; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "retained", nativeName = "retained_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["retained.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                semantics = new()
                {
                    callbackParameters = [new()
                    {
                        function = "Register", parameter = "callback", lifetime = NativeLifetimeKind.Registration, retained = true,
                        threading = NativeCallbackThreading.AnyThread, userDataParameter = "state", callableUserDataIndex = 1, failureReturn = "-7",
                        unregisterFunction = "StopAndJoin", unregisterQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations,
                        registration = paired ? new()
                        {
                            kind = NativeCallbackRegistrationKind.CallbackAndUserData, unregisterCallbackParameter = "callback", unregisterUserDataParameter = "state",
                            successfulReturn = "1", failureResultQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations, releaseThreadDomain = "any"
                        } : new() { unregisterHandleParameter = "handle", nullResultQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations, releaseThreadDomain = "any" }
                    }]
                },
                api = new() { @namespace = "Generated.Retained", name = "RetainedApi", importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                 cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            Assert.Contains(target.interop.functions.SelectMany(function => function.parameters), parameter => parameter.callback?.registration is not null);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            string context = sourceRuntime ? "RetainedApiContext" : "RetainedApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string nativeIdentity = paired ? "(void*)handle" : "handle";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, "retained_fixture", $$"""
                using System;
                using Generated.Retained;
                using ApiContext = Generated.Retained.{{context}};
                public static unsafe class Probe {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        int inline=0;
                        using(var registration=api.Register(value => { inline++; GC.Collect(); GC.WaitForPendingFinalizers(); return value+3; },0)) {
                            if(inline!=1 || api.LiveCount()!=1) return 1;
                            if(registration.Invoke(handle => api.Raw.Trigger({{nativeIdentity}},38))!=42 || inline!=2) return 2;
                        }
                        if(api.LiveCount()!=0 || api.IdentityFailures()!=0) return 3;
                        var marker=new ApplicationException("retained callback marker");
                        using(var registration=api.Register(value => throw marker,0)) {
                            if(!ReferenceEquals(marker,registration.TakeCallbackException())) return 4;
                            if(registration.Invoke(handle => api.Raw.Trigger({{nativeIdentity}},20))!=-6 || api.LastResult()!=-7) return 5;
                            if(!ReferenceEquals(marker,registration.TakeCallbackException())) return 6;
                        }
                        IDisposable? self=null;
                        using(var registration=api.Register(value => { self?.Dispose(); return value+3; },0)) {
                            self=registration;
                            if(registration.Invoke(handle => api.Raw.Trigger({{nativeIdentity}},38))!=42) return 7;
                            if(!registration.completion.Wait(TimeSpan.FromSeconds(10))) return 8;
                            if(registration.closeFailure is not null || api.LiveCount()!=0 || api.IdentityFailures()!=0) return 9;
                            try {registration.Invoke(handle => api.Raw.Trigger({{nativeIdentity}},1));return 10;} catch(ObjectDisposedException){}
                        }
                        try {api.Register(value => value,-1);return 11;} catch(InvalidOperationException){}
                        if(api.LiveCount()!=0) return 12;
                        using(var closing=api.Register(value => { if(value==38) api.Dispose(); return value+3; },0)) {
                            if(closing.Invoke(handle => api.Raw.Trigger({{nativeIdentity}},38))!=42) return 13;
                            try {api.LiveCount();return 14;} catch(ObjectDisposedException){}
                            closing.DisposeAsync().AsTask().GetAwaiter().GetResult();
                            if(!closing.completion.IsCompletedSuccessfully) return 15;
                        }
                        return 42;
                    }
                }
                """, "Probe", root, sourceRuntime));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
