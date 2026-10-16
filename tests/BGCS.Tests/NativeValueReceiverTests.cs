using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeValueReceiverTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ValueMembersPreserveOriginalInRefAndSpanStorageAcrossGcAndContexts(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-value-receiver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "value.h"), """
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                typedef struct NativeValue { int value; int tag; } NativeValue;
                API int value_add(NativeValue* self,int delta);
                API int value_read(const NativeValue* self,const int* same);
                API void value_reset(NativeValue* self,int value);
                API int value_alias(NativeValue* self,int* same,int* buffer,int count);
                API int value_validate(int prefix,const NativeValue* self,int value,int tag);
                API int value_hold(NativeValue* self,int mode);
                API int value_entered(void);
                API void value_release(void);
                API const void* value_held_address(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "value.c"), """
                #include "value.h"
                #if defined(_WIN32)
                #define WIN32_LEAN_AND_MEAN
                #include <windows.h>
                static volatile LONG hold_entered,hold_released;
                #define READ_GATE(flag) InterlockedCompareExchange(&(flag),0,0)
                #define WRITE_GATE(flag,value) InterlockedExchange(&(flag),(value))
                #else
                #include <stdatomic.h>
                static _Atomic int hold_entered,hold_released;
                #define READ_GATE(flag) atomic_load_explicit(&(flag),memory_order_acquire)
                #define WRITE_GATE(flag,value) atomic_store_explicit(&(flag),(value),memory_order_release)
                #endif
                static const void* held_address;
                API int value_add(NativeValue* self,int delta){self->value+=delta;return self->value;}
                API int value_read(const NativeValue* self,const int* same){return same==&self->value?self->value+self->tag:-101;}
                API void value_reset(NativeValue* self,int value){self->value=value;self->tag=0;}
                API int value_alias(NativeValue* self,int* same,int* buffer,int count){
                    if(same!=&self->value || buffer!=&self->value || count!=2)return -102;
                    ++*same;buffer[1]+=*same;return self->value+self->tag;
                }
                API int value_validate(int prefix,const NativeValue* self,int value,int tag){return prefix==7 && self->value==value && self->tag==tag?42:-103;}
                API int value_hold(NativeValue* self,int mode){
                    if(mode<0){WRITE_GATE(hold_released,0);held_address=self;WRITE_GATE(hold_entered,1);while(!READ_GATE(hold_released)){};}
                    int result=self->value;held_address=0;WRITE_GATE(hold_entered,0);return result;
                }
                API int value_entered(void){return (int)READ_GATE(hold_entered);}
                API void value_release(void){WRITE_GATE(hold_released,1);}
                API const void* value_held_address(void){return held_address;}
                """);
            string[] functions = ["value_add", "value_read", "value_reset", "value_alias", "value_validate", "value_hold"];
            ValueParameterConfiguration Value(string function, string parameter, NativeParameterDirection direction) => new()
            { function = function, parameter = parameter, direction = direction, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false };
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "values", nativeName = "values_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["value.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false,
                    selection = new() { includeDeclarations = functions.Concat(new[] { "value_entered", "value_release", "value_held_address" }).ToImmutableArray() } },
                api = new()
                {
                    @namespace = "Generated.Values", name = "ValueApi", importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance, nestGeneratedTypesInApi = sourceRuntime,
                    names = ImmutableDictionary<string, string>.Empty.Add("value_add", "Add").Add("value_read", "Read").Add("value_reset", "Reset")
                        .Add("value_alias", "Alias").Add("value_validate", "Validate").Add("value_hold", "Hold")
                        .Add("value_entered", "Entered").Add("value_release", "Release").Add("value_held_address", "HeldAddress"),
                    records = [new("NativeValue") { emitSizeConstant = true, fields = [new("value") { name = "Value" }, new("tag") { name = "Tag" }] }],
                    memberFunctions = functions.Select(function => new ManagedReceiverRequest(function, "NativeValue", "self") { kind = ManagedReceiverKind.Value }).ToImmutableArray()
                },
                semantics = new()
                {
                    valueParameters = functions.Select(function => Value(function, "self", function is "value_read" or "value_validate"
                        ? NativeParameterDirection.Input : NativeParameterDirection.InputOutput))
                        .Concat(new[] { Value("value_read", "same", NativeParameterDirection.Input), Value("value_alias", "same", NativeParameterDirection.InputOutput) }).ToImmutableArray(),
                    bufferParameters = [new() { function = "value_alias", parameter = "buffer", capacityParameter = "count", direction = NativeParameterDirection.InputOutput,
                        lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false, lengthUnit = NativeLengthUnit.Elements }]
                },
                nativeBuild = new() { mode = "sources", sources = ["value.c"] }, runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.generation.targetModels);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin")));
            string context = sourceRuntime ? "ValueApiContext" : "ValueApi";
            string carrier = sourceRuntime ? "ValueApi.NativeValue" : "NativeValue";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string shared = sourceRuntime ? (mode == BindingImportMode.FunctionTable ? "ValueApi.Load(binary);" : "ValueApi.Create();")
                + "if(ValueApi.Add(ref values[0],0)!=42)return 30;ValueApi.Dispose();" : "";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary,
                configuration.library.nativeName, $$"""
                using System;
                using System.Runtime.InteropServices;
                using System.Threading;
                using System.Threading.Tasks;
                using Generated.Values;
                using ApiContext=Generated.Values.{{context}};
                using Carrier=Generated.Values.{{carrier}};
                public static unsafe class ValueConsumer {
                    static int ReadOnly(in Carrier value,ApiContext context,in int same)=>value.Read(context,in same);
                    public static int Run(string binary){
                        using var first={{factory}};
                        using var second={{factory}};
                        Carrier local=default;local.Reset(first,40);
                        if(local.Add(first,2)!=42 || ReadOnly(in local,second,in local.Value)!=42)return 1;
                        if(sizeof(Carrier)!=8 || Carrier.NativeSize!=8 || Marshal.OffsetOf<Carrier>("Tag").ToInt32()!=4)return 2;
                        Carrier[] values=new Carrier[1];values[0].Reset(first,40);
                        if(values[0].Add(second,2)!=42)return 3;
                        values[0].Reset(first,20);
                        if(values[0].Alias(first,ref values[0].Value,MemoryMarshal.Cast<Carrier,int>(values.AsSpan()))!=42)return 4;
                        if(values[0].Value!=21 || values[0].Tag!=21 || ReadOnly(in values[0],second,in values[0].Value)!=42)return 5;
                        if(values[0].Validate(first,7,21,21)!=42)return 6;
                        first.Dispose();
                        try{values[0].Add(first,1);return 7;}catch(ObjectDisposedException){}
                        if(values[0].Read(second,in values[0].Value)!=42)return 8;
                        values[0].Reset(second,42);
                        Task<int> pending=Task.Run(()=>values[0].Hold(second,-1));
                        Exception? gateFailure=null;
                        try{
                            if(!SpinWait.SpinUntil(()=>second.Raw.Entered()!=0 || pending.IsCompleted,TimeSpan.FromSeconds(10)))throw new TimeoutException("Native value frame did not enter.");
                            if(pending.IsCompleted)throw new InvalidOperationException("Native value frame returned before GC.");
                            for(int i=0;i<3;++i){_ = new byte[1024*1024];GC.Collect(2,GCCollectionMode.Forced,true,true);GC.WaitForPendingFinalizers();}
                            fixed(Carrier* address=&values[0]){if(second.Raw.HeldAddress()!=(void*)address)throw new InvalidOperationException("Native receiver address no longer matches original array storage.");}
                        }catch(Exception error){gateFailure=error;}finally{second.Raw.Release();}
                        int held=pending.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                        if(gateFailure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(gateFailure);
                        if(held!=42 || values[0].Value!=42 || values[0].Tag!=0)return 11;
                        values[0].Reset(second,42);
                        fixed(Carrier* raw=&values[0]){if(second.Raw.Add(raw,0)!=42)return 12;}
                        try{values[0].Add(null!,0);return 13;}catch(ArgumentNullException){}
                        {{shared}}
                        return 42;
                    }
                }
                """, "ValueConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
