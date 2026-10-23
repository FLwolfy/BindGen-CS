using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeAsyncOperationTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task NativeTerminalJoinRetainsMemoryThroughCompletionTailAndCancellationAcknowledgement(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-async-operation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "operation.h"), """
                #pragma once
                #if defined(_WIN32)
                #define OP_API __declspec(dllexport)
                #else
                #define OP_API __attribute__((visibility("default")))
                #endif
                #ifdef __cplusplus
                extern "C" {
                #endif
                typedef struct Operation Operation;
                typedef void (*Completion)(int result, int status, void* state);
                OP_API Operation* Start(Completion callback, int* values, int count, int mode, void* state);
                OP_API void Cancel(Operation* handle);
                OP_API void StopAndJoin(Operation* handle);
                OP_API void ReleaseAll(void);
                OP_API int LiveCount(void);
                OP_API int JoinedCount(void);
                #ifdef __cplusplus
                }
                #endif
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "operation.cpp"), """
                #include "operation.h"
                #include <algorithm>
                #include <atomic>
                #include <chrono>
                #include <condition_variable>
                #include <mutex>
                #include <thread>
                #include <vector>
                struct Operation {
                    Completion callback; int* values; int count; int mode; void* state;
                    std::mutex gate; std::condition_variable changed; bool go=false, cancelled=false; std::thread thread;
                };
                static std::mutex registry_gate;
                static std::vector<Operation*> registry;
                static std::atomic<int> joined{0};
                OP_API Operation* Start(Completion callback,int* values,int count,int mode,void* state) {
                    if(mode<0) return nullptr;
                    Operation* handle=new Operation{callback,values,count,mode,state};
                    {std::lock_guard<std::mutex> lock(registry_gate); registry.push_back(handle);}
                    if(mode==3) {int sum=0;for(int i=0;i<count;++i)sum+=values[i];callback(sum,0,state);}
                    handle->thread=std::thread([handle] {
                        bool cancelled;
                        {std::unique_lock<std::mutex> lock(handle->gate);handle->changed.wait(lock,[&]{return handle->go;});cancelled=handle->cancelled;}
                        int sum=0;for(int i=0;i<handle->count;++i)sum+=handle->values[i];
                        if(handle->mode!=3) handle->callback(sum,cancelled?1:handle->mode==2?9:0,handle->state);
                        // Completion entered managed code, but native still uses the pinned storage and executable module.
                        handle->values[0]+=1000;
                        std::this_thread::sleep_for(std::chrono::milliseconds(25));
                    });
                    return handle;
                }
                OP_API void Cancel(Operation* handle) {std::lock_guard<std::mutex> lock(handle->gate);handle->cancelled=true;}
                OP_API void StopAndJoin(Operation* handle) {
                    handle->thread.join();
                    {std::lock_guard<std::mutex> lock(registry_gate);registry.erase(std::find(registry.begin(),registry.end(),handle));}
                    delete handle; ++joined;
                }
                OP_API void ReleaseAll(void) {
                    std::lock_guard<std::mutex> lock(registry_gate);
                    for(Operation* handle:registry) {std::lock_guard<std::mutex> item(handle->gate);handle->go=true;handle->changed.notify_all();}
                }
                OP_API int LiveCount(void) {std::lock_guard<std::mutex> lock(registry_gate);return(int)registry.size();}
                OP_API int JoinedCount(void) {return joined;}
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "operations", nativeName = "operation_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["operation.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                semantics = new()
                {
                    callbackParameters = [new()
                    {
                        function = "Start", parameter = "callback", lifetime = NativeLifetimeKind.Operation, retained = true,
                        threading = NativeCallbackThreading.AnyThread, userDataParameter = "state", callableUserDataIndex = 2,
                        unregisterFunction = "StopAndJoin", unregisterQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations,
                        registration = new() { unregisterHandleParameter = "handle", nullResultQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations, releaseThreadDomain = "any" }
                    }],
                    bufferParameters = [new()
                    {
                        function = "Start", parameter = "values", lengthParameter = "count", lengthUnit = NativeLengthUnit.Elements,
                        direction = NativeParameterDirection.InputOutput, lifetime = NativeLifetimeKind.Operation, retainsPointer = true, allowsNull = false
                    }],
                    asyncOperations = [new()
                    {
                        function = "Start", completionCallback = "callback", completionQuiescence = NativeQuiescenceKind.NoNewInvocations,
                        resultArgumentIndex = 0, statusArgumentIndex = 1, successStatus = "0", cancellationStatus = "1",
                        cancelFunction = "Cancel", cancelHandleParameter = "handle", retainedParameters = ["values"]
                    }]
                },
                api = new()
                {
                    @namespace = "Generated.Operations",
                    name = "OperationApi",
                    importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            AsyncContract contract = Assert.Single(target.interop.functions.Where(function => function.asynchronous is not null)).asynchronous!;
            Assert.Single(contract.retainedParameters);
            Assert.NotNull(contract.cancelFunction);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            string context = sourceRuntime ? "OperationApiContext" : "OperationApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, "operation_fixture", $$"""
                using System;
                using System.Buffers;
                using System.Runtime.InteropServices;
                using System.Threading;
                using System.Threading.Tasks;
                using Generated.Operations;
                using ApiContext = Generated.Operations.{{context}};
                public static unsafe class Probe {
                    private sealed class TrackedMemory : MemoryManager<int> {
                        private readonly int[] data = [20,22];
                        private int pins;
                        public int pinCount => Volatile.Read(ref pins);
                        public override Span<int> GetSpan() => data;
                        public override MemoryHandle Pin(int elementIndex=0) {
                            var handle=GCHandle.Alloc(data,GCHandleType.Pinned); Interlocked.Increment(ref pins);
                            return new MemoryHandle((void*)(handle.AddrOfPinnedObject()+elementIndex*sizeof(int)),handle,this);
                        }
                        public override void Unpin() => Interlocked.Decrement(ref pins);
                        protected override void Dispose(bool disposing) { if(pinCount!=0) throw new InvalidOperationException("Storage disposed before native join"); }
                    }
                    public static int Run(string binary) {
                        using var api={{factory}};
                        try {
                        using(var memory=new TrackedMemory()) {
                            using var operation=api.StartAsync(memory.Memory,0);
                            if(memory.pinCount!=1 || operation.task.IsCompleted) return 1;
                            GC.Collect();GC.WaitForPendingFinalizers(); api.ReleaseAll();
                            if(operation.task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()!=42) return 2;
                            if(memory.pinCount!=0 || memory.GetSpan()[0]!=1020 || api.LiveCount()!=0) return 3;
                        }
                        using(var memory=new TrackedMemory()) {
                            using var operation=api.StartAsync(memory.Memory,0);
                            operation.Dispose();
                            if(memory.pinCount!=1 || operation.task.IsCompleted || !operation.isCancellationRequested) return 4;
                            api.ReleaseAll();
                            try {operation.task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();return 5;} catch(OperationCanceledException){}
                            if(memory.pinCount!=0 || memory.GetSpan()[0]!=1020 || api.LiveCount()!=0) return 6;
                        }
                        using(var memory=new TrackedMemory()) {
                            using var operation=api.StartAsync(memory.Memory,2); api.ReleaseAll();
                            try {operation.task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();return 7;} catch(InvalidOperationException){}
                            if(memory.pinCount!=0 || memory.GetSpan()[0]!=1020) return 8;
                        }
                        using(var memory=new TrackedMemory()) {
                            using var operation=api.StartAsync(memory.Memory,3);
                            if(memory.pinCount!=1 || operation.task.IsCompleted) return 9;
                            api.ReleaseAll();
                            if(operation.task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()!=42 || memory.pinCount!=0) return 10;
                        }
                        using(var memory=new TrackedMemory()) {
                            try {api.StartAsync(memory.Memory,-1);return 11;} catch(InvalidOperationException){}
                            if(memory.pinCount!=0 || api.LiveCount()!=0) return 12;
                        }
                        using(var memory=new TrackedMemory()) {
                            using var operation=api.StartAsync(memory.Memory,0); api.Dispose();
                            try {api.LiveCount();return 13;} catch(ObjectDisposedException){}
                            using(var control={{factory}}) control.ReleaseAll();
                            if(operation.task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()!=42 || memory.pinCount!=0) return 14;
                            api.DisposeAsync().AsTask().GetAwaiter().GetResult();
                        }
                        return 42;
                        } finally {
                            using var cleanup={{factory}}; cleanup.ReleaseAll();
                            for(int attempt=0;attempt<500 && cleanup.LiveCount()!=0;attempt++) Thread.Sleep(10);
                            if(cleanup.LiveCount()!=0) throw new InvalidOperationException("The native fixture did not drain its operation threads");
                        }
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
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
