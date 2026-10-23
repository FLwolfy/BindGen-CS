using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Interop;
using BGCS.Core.Requests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ValueStringLifetimeTests
{
    [Fact]
    public async Task ActualFunctionTableCallsKeepCleanupOnTheCreatingThreadAcrossClosureAndFailure()
    {
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(ValueStringResultTests.Project(ValueStringResultTests.Module(NativeTextEncoding.Utf8)),
            new EmissionRequest("value_strings", sink) { maximumValueStringCodeUnits = 73 }, CancellationToken.None);
        const string consumer = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Text;
            using BGCS.Runtime;
            using Generated.ValueStrings;
            public static unsafe class ValueLifetimeConsumer {
                private static int mode,creatorThread,threadMismatch,created,released,copied;
                private static Api? current;
                private static nint abandoned;
                private static readonly byte[] text=[65,0,240,159,152,128,66];
                public static int Run(){
                    for(int test=0;test<9;test++){
                        mode=test;created=released=copied=threadMismatch=0;abandoned=0;
                        Provider provider=new();Api api=Api.FromProvider(provider);current=api;
                        Exception? failure=null;
                        try{
                            string result=api.Make();
                            if(test is 1 or 2 or 3 or 5 or 6 or 7)throw new Exception("missing expected native text failure");
                            if(result!=(test==8?string.Empty:"A\0😀B"))throw new Exception("copied native text changed");
                        }
                        catch(Exception caught){
                            failure=caught;
                            if(test==1&&caught is not ArgumentOutOfRangeException)throw;
                            if(test==2&&caught is not InvalidOperationException)throw;
                            if(test==3&&caught is not DecoderFallbackException)throw;
                            if(test==5&&caught is not NativeCallException)throw;
                            if(test is 6 or 7){
                                if(caught is not AggregateException aggregate||aggregate.InnerExceptions.Count!=2)throw;
                                if(test==6&&aggregate.InnerExceptions[0] is not DecoderFallbackException)throw;
                                if(test==7&&aggregate.InnerExceptions[0] is not NativeCallException)throw;
                                if(aggregate.InnerExceptions[1] is not NativeCallException)throw;
                            }
                            if(test is 0 or 4 or 8)throw;
                        }
                        if(created!=1||released!=1||threadMismatch!=0)throw new Exception("creating-call cleanup affinity or exact count changed");
                        if((test is 1 or 7)&&copied!=0)throw new Exception("native copy entered despite a failed count/bound");
                        api.Dispose();
                        if(test is 5 or 6 or 7){
                            try{api.DisposeAsync().AsTask().GetAwaiter().GetResult();throw new Exception("unresolved cleanup unload did not fault");}
                            catch(NativeCallException closeFailure){
                                Exception original=failure is AggregateException aggregate?aggregate.InnerExceptions[1]:failure!;
                                if(!ReferenceEquals(original,closeFailure))throw;
                            }
                            if(provider.releases!=0)throw new Exception("provider unloaded under unresolved native cleanup");
                            NativeMemory.Free((void*)abandoned);
                        }else{
                            api.DisposeAsync().AsTask().GetAwaiter().GetResult();
                            if(provider.releases!=1)throw new Exception("provider release count changed");
                        }
                        current=null;
                    }
                    return 42;
                }
                private static void CheckThread(){if(creatorThread!=Environment.CurrentManagedThreadId)threadMismatch++;}
                [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])]
                private static int Make(Holder** result,Error** error){
                    creatorThread=Environment.CurrentManagedThreadId;created++;*error=null;
                    *result=(Holder*)NativeMemory.Alloc(1);if(*result==null)return 1;
                    if(mode==4)current!.Dispose();return 0;
                }
                [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])]
                private static int Count(Holder* self,ulong* result,Error** error){
                    CheckThread();*error=null;if(mode==7)return 7;*result=mode==1?74UL:mode==8?0UL:7UL;return 0;
                }
                [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])]
                private static int Get(Holder* self,ulong index,byte* result,Error** error){*error=null;*result=0;return 0;}
                [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])]
                private static int Copy(Holder* self,byte* destination,ulong capacity,ulong* written,Error** error){
                    CheckThread();copied++;*error=null;*written=0;
                    if(mode==8)return destination==null&&capacity==0?0:8;
                    if(capacity!=7||destination==null)return 2;
                    for(int index=0;index<7;index++)destination[index]=text[index];
                    if(mode is 3 or 6)destination[0]=255;
                    *written=mode==2?6UL:7UL;return 0;
                }
                [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])]
                private static int Destroy(Holder* self,Error** error){
                    CheckThread();released++;*error=null;
                    if(mode is 5 or 6 or 7){abandoned=(nint)self;return 9;}
                    NativeMemory.Free(self);return 0;
                }
                [UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])]
                private static void FreeError(Error* error){NativeMemory.Free(error);}
                private sealed class Provider:INativeModuleProvider{
                    internal int releases;
                    public bool TryGetExport(string name,out nint address){address=name switch{
                        "Make"=>(nint)(delegate* unmanaged[Cdecl]<Holder**,Error**,int>)&Make,
                        "length"=>(nint)(delegate* unmanaged[Cdecl]<Holder*,ulong*,Error**,int>)&Count,
                        "get"=>(nint)(delegate* unmanaged[Cdecl]<Holder*,ulong,byte*,Error**,int>)&Get,
                        "copy"=>(nint)(delegate* unmanaged[Cdecl]<Holder*,byte*,ulong,ulong*,Error**,int>)&Copy,
                        "destroy"=>(nint)(delegate* unmanaged[Cdecl]<Holder*,Error**,int>)&Destroy,
                        "error_free"=>(nint)(delegate* unmanaged[Cdecl]<Error*,void>)&FreeError,
                        _=>0};return address!=0;}
                    public void Dispose(){releases++;}
                }
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create("ScopedNativeText_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, new(LanguageVersion.CSharp14), pair.Key)).Append(CSharpSyntaxTree.ParseText(consumer, new(LanguageVersion.CSharp14))),
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.Equal(42, Assembly.Load(image.ToArray()).GetType("ValueLifetimeConsumer", throwOnError: true)!.GetMethod("Run")!.Invoke(null, null));
    }
}
