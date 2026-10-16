using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using BGCS.Platforms;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeStringResultTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ExactTextCopiesUseMatchingNativeCleanupAcrossSuccessFailureAndContextClosure(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-string-result-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "text.h"), Header);
            await File.WriteAllTextAsync(Path.Combine(root, "text.c"), Native);
            string[] owned8 = ["text8", "nullable8", "fixed8", "input8", "output8", "overflow8", "close8"];
            string[] owned16 = ["text16", "big16", "bytes16"];
            string[] owned32 = ["text32", "big32"];
            string[] borrowed = ["borrow8", "unknown8", "wrong_width"];
            var strings = owned8.Select(function => Rule(function, NativeTextEncoding.Utf8))
                .Concat(owned16.Select(function => Rule(function, NativeTextEncoding.Utf16)))
                .Concat(owned32.Select(function => Rule(function, NativeTextEncoding.Utf32)))
                .Append(Rule("borrow8", NativeTextEncoding.Utf8)).Append(Rule("unknown8", NativeTextEncoding.Utf8))
                .Append(Rule("wrong_width", NativeTextEncoding.Utf16)).Select(rule => rule.function switch
                {
                    "nullable8" or "borrow8" => rule with { allowsNull = true },
                    "big16" or "big32" => rule with { byteOrder = NativeTextByteOrder.BigEndian },
                    "fixed8" => rule with { zeroTerminated = false, fixedLength = 3 },
                    "input8" => rule with { zeroTerminated = false, lengthParameter = "count", lengthDirection = NativeParameterDirection.Input },
                    "output8" or "overflow8" or "bytes16" => rule with { zeroTerminated = false, lengthParameter = "length", lengthDirection = NativeParameterDirection.Output,
                        lengthOutputCallBounded = true, lengthUnit = rule.function == "bytes16" ? NativeLengthUnit.Bytes : NativeLengthUnit.CodeUnits },
                    _ => rule
                }).ToImmutableArray();
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "string-result", nativeName = "string_result_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["text.h"], includeTransitivelyReferencedHeaders = false, parseMacros = false },
                api = new()
                {
                    @namespace = "Generated.StringResults", name = "TextApi", importMode = mode,
                    names = owned8.Concat(owned16).Concat(owned32).Concat(borrowed).ToImmutableDictionary(function => function,
                        function => function switch { "wrong_width" => "WrongWidth", _ => char.ToUpperInvariant(function[0]) + function[1..] }, StringComparer.Ordinal),
                    functionAliases = [new("text8", "ReadAlias")], functionContainers = [new("Texts", [new("text8", "Read")])],
                    functions = [new("text8") { defaults = [new("mode", new(ManagedDefaultValueKind.Integer, "0"))] }]
                },
                semantics = new()
                {
                    ownedResults = owned8.Select(function => Owned(function, "release8")).Concat(owned16.Select(function => Owned(function, "release16")))
                        .Concat(owned32.Select(function => Owned(function, "release32"))).ToImmutableArray(),
                    borrowedResults = new[] { "borrow8", "wrong_width" }.Select(function => new BorrowedResultConfiguration { function = function, lifetime = NativeLifetimeKind.Module }).ToImmutableArray(),
                    stringResults = strings,
                    stringParameters = [new() { function = "close8", parameter = "trigger", encoding = NativeTextEncoding.Utf8, codeUnitWidth = 1,
                        byteOrder = NativeTextByteOrder.Native, direction = NativeParameterDirection.Input, lengthUnit = NativeLengthUnit.CodeUnits,
                        lifetime = NativeLifetimeKind.Call, zeroTerminated = true, allowsNull = false, retainsPointer = false }]
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" }, output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                nativeBuild = new() { mode = "sources", sources = ["text.c"] }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.generation.targetModels);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(target.managed!, new(configuration.library.nativeName, new NoWrites())
            {
                importMode = mode, runtimeNamespace = sourceRuntime ? "Generated.StringResults.Runtime" : "BGCS.Runtime", includeRuntimeSource = sourceRuntime,
                managedRuntime = ManagedRuntimeResolver.Resolve(target.resolvedTarget!, ManagedRuntimeKind.CoreClr)
            });
            Assert.Equal(13, plan.stringResults.Length);
            Assert.Equal(2, plan.ownedProjectionOmissions.Length);
            Assert.Empty(plan.directOwnedFactories);
            Assert.Empty(plan.directBorrowedFactories);
            var sources = Directory.GetFiles(Path.Combine(targetRoot, "managed"), "*.cs", SearchOption.AllDirectories)
                .Select(path => (Path: path, Text: File.ReadAllText(path))).ToArray();
            new EmittedInteropContractReader().Verify(plan, sources, configuration.library.nativeName);
            var mutation = sources.Select(source => (source.Path, source.Text.Replace("Utf8Codec.Decode(", "Utf8Codec.DecodeLength(", StringComparison.Ordinal))).ToArray();
            Assert.Contains(sources, source => source.Text.Contains("Utf8Codec.Decode(", StringComparison.Ordinal));
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, mutation, configuration.library.nativeName));
            string create = mode == BindingImportMode.FunctionTable ? "TextApi.Load(binary)" : "TextApi.Create()";
            string createAllocator = mode == BindingImportMode.FunctionTable ? "TextApi.Load(binary, allocator)" : "TextApi.Create(allocator)";
            string runtime = sourceRuntime ? "Generated.StringResults.Runtime" : "BGCS.Runtime";
            string consumer = $$"""
                using System;
                using System.Runtime.InteropServices;
                using System.Text;
                using Generated.StringResults;
                using Runtime = {{runtime}};
                public static unsafe class TextResultConsumer {
                    public static int Run(string binary) {
                        using var api = {{create}};
                        Check(api.Text8()=="A😀B" && api.Text16(0)=="A😀B" && api.Text32(0)=="A😀B", "unicode");
                        Check(api.Big16()=="A😀B" && api.Big32()=="A😀B", "byte order");
                        Check(api.Fixed8()=="A\0B" && api.Input8(3)=="A\0B" && api.Output8(0)=="A\0B" && api.Bytes16(0)=="A\0B" && api.Overflow8(0)=="A\0B", "exact extents");
                        Check(api.Input8(0)==string.Empty && api.Text8(4)==string.Empty, "empty");
                        Check(api.Nullable8(3)==null && api.Borrow8(1)==null && api.Borrow8(0)=="A😀B", "nullable module borrow");
                        Check(api.ReadAlias(0)=="A😀B" && api.Texts.Read(0)=="A😀B", "forwarders");
                        Expect<DecoderFallbackException>(api,()=>api.Text8(1)); Expect<DecoderFallbackException>(api,()=>api.Text16(1)); Expect<DecoderFallbackException>(api,()=>api.Text32(1));
                        Expect<ArgumentException>(api,()=>api.Text8(2)); Expect<ArgumentException>(api,()=>api.Text16(2)); Expect<ArgumentException>(api,()=>api.Text32(2));
                        Expect<InvalidOperationException>(api,()=>api.Text8(3),0);
                        Expect<ArgumentOutOfRangeException>(api,()=>api.Input8(-1)); Expect<ArgumentOutOfRangeException>(api,()=>api.Input8(9));
                        Expect<ArgumentOutOfRangeException>(api,()=>api.Output8(1)); Expect<ArgumentOutOfRangeException>(api,()=>api.Output8(2));
                        Expect<DecoderFallbackException>(api,()=>api.Output8(4)); Expect<ArgumentException>(api,()=>api.Bytes16(1));
                        Expect<ArgumentOutOfRangeException>(api,()=>api.Bytes16(2)); Expect<OverflowException>(api,()=>api.Overflow8(1));
                        int temporaryReleases=0;
                        TextApi? closing=null;
                        Func<Runtime.NativeModuleSession,Runtime.NativeAllocator> allocator=session=>new Runtime.NativeAllocator("close-during-text-input",
                            (length,alignment)=>{closing!.Dispose();return (nint)NativeMemory.Alloc(length);},
                            (address,length,alignment)=>{++temporaryReleases;NativeMemory.Free((void*)address);},Runtime.NativeAllocatorThreadDomain.AnyThread,session);
                        closing={{createAllocator}};
                        int before=api.Raw.Releases();
                        using(closing) Check(closing.Close8("close")=="A😀B", "active call after context closes");
                        Check(api.Raw.Releases()==before+1 && temporaryReleases==1, "matching cleanup after context closes");
                        Check(api.Raw.Live()==0 && api.Raw.WrongRelease()==0,"native ownership domains");
                        return 42;
                    }
                    private static void Expect<T>(TextApi api,Action action,int released=1) where T:Exception {
                        int before=api.Raw.Releases();bool caught=false;try{action();}catch(T){caught=true;}
                        Check(caught && api.Raw.Releases()==before+released && api.Raw.Live()==0,typeof(T).Name+" and matching release");
                    }
                    private static void Check(bool condition,string name){if(!condition)throw new InvalidOperationException("Text result check failed: "+name);}
                }
                """;
            string native = Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin")));
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), native,
                configuration.library.nativeName, consumer, "TextResultConsumer", root, sourceRuntime));
            if (mode == BindingImportMode.FunctionTable && !sourceRuntime)
            {
                var before = Directory.GetFiles(result.outputRoot, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(result.outputRoot, path), File.ReadAllBytes, StringComparer.Ordinal);
                await using BindingHost tampering = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot())
                    .AddArtifactProcessor("string-result.tamper", 0, () => new Tamper()).Build();
                BuildResult rejected = await tampering.BuildAsync(new(new(configuration, root)));
                Assert.False(rejected.success);
                Assert.Contains("string result", rejected.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
                var after = Directory.GetFiles(result.outputRoot, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(result.outputRoot, path), File.ReadAllBytes, StringComparer.Ordinal);
                Assert.Equal(before.Keys.Order(), after.Keys.Order());
                foreach (string path in before.Keys) Assert.Equal(before[path], after[path]);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static StringResultConfiguration Rule(string function, NativeTextEncoding encoding) => new()
    { function = function, encoding = encoding, allowsNull = false, zeroTerminated = true, maximumCodeUnits = 8 };
    private static OwnedResultConfiguration Owned(string function, string release) => new()
    { function = function, releaseFunction = release, releaseDomain = "width-checked-native-heap", releaseThreadDomain = "any" };
    private sealed class NoWrites : IArtifactSink
    { public string WriteText(string relativePath, string content) => throw new InvalidOperationException("Planning must not write artifacts."); }
    private sealed class Tamper : IArtifactProcessor
    {
        public string id => "string-result.tamper";
        public int priority => 0;
        public async ValueTask ProcessAsync(ArtifactProcessingRequest request, CancellationToken cancellationToken)
        {
            foreach (ArtifactDescriptor artifact in request.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp"))
            {
                string text = await request.content.ReadTextAsync(artifact.relativePath, cancellationToken);
                string changed = text.Replace("Utf8Codec.Decode(", "Utf8Codec.DecodeLength(", StringComparison.Ordinal);
                if (changed != text) await request.content.ReplaceTextAsync(artifact.relativePath, changed, cancellationToken);
            }
        }
    }
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }

    private const string Header = """
        #if defined(_WIN32)
        #define API __declspec(dllexport)
        #else
        #define API __attribute__((visibility("default")))
        #endif
        API unsigned char* text8(int mode);
        API unsigned char* nullable8(int mode);
        API unsigned short* text16(int mode);
        API unsigned int* text32(int mode);
        API unsigned short* big16(void);
        API unsigned int* big32(void);
        API unsigned char* fixed8(void);
        API unsigned char* input8(int count);
        API unsigned char* output8(int mode,int* length);
        API unsigned short* bytes16(int mode,int* length);
        API unsigned char* overflow8(int mode,unsigned long long* length);
        API unsigned char* close8(const char* trigger);
        API unsigned char* borrow8(int missing);
        API unsigned char* unknown8(void);
        API unsigned char* wrong_width(void);
        API void release8(unsigned char* value);
        API void release16(unsigned short* value);
        API void release32(unsigned int* value);
        API int releases(void);
        API int live(void);
        API int wrong_release(void);
        """;
    private const string Native = """
        #include "text.h"
        #include <stdlib.h>
        #include <string.h>
        static int release_count,live_count,wrong_count;
        static unsigned char valid8[]={65,0xf0,0x9f,0x98,0x80,66,0};
        static void* copy(int width,const void* source,int bytes) {
            int* allocation=(int*)malloc((size_t)bytes+8); if(!allocation)return 0;
            allocation[0]=width;allocation[1]=0;void* result=allocation+2;
            memcpy(result,source,(size_t)bytes);++live_count;return result;
        }
        static void drop(void* value,int width) {if(!value)return;int* allocation=((int*)value)-2;
            if(allocation[0]!=width)++wrong_count;++release_count;--live_count;free(allocation);}
        API unsigned char* text8(int mode) {
            unsigned char bytes[8];memset(bytes,65,8);
            if(mode==3)return 0;if(mode==4){bytes[0]=0;return (unsigned char*)copy(1,bytes,1);}
            if(mode==1){bytes[0]=0xc0;bytes[1]=0xaf;bytes[2]=0;return (unsigned char*)copy(1,bytes,3);}
            return (unsigned char*)copy(1,mode==2?bytes:valid8,mode==2?8:7);
        }
        API unsigned char* nullable8(int mode){return text8(mode);}
        API unsigned short* text16(int mode) {
            unsigned short bytes[8]={65,0xd83d,0xde00,66,0,65,65,65};
            if(mode==3)return 0;if(mode==1){bytes[0]=0xd800;bytes[1]=0;}if(mode==2)for(int i=0;i<8;++i)bytes[i]=65;
            return (unsigned short*)copy(2,bytes,16);
        }
        API unsigned int* text32(int mode) {
            unsigned int bytes[8]={65,0x1f600,66,0,65,65,65,65};
            if(mode==3)return 0;if(mode==1){bytes[0]=0x110000;bytes[1]=0;}if(mode==2)for(int i=0;i<8;++i)bytes[i]=65;
            return (unsigned int*)copy(4,bytes,32);
        }
        API unsigned short* big16(void){unsigned char bytes[]={0,65,0xd8,0x3d,0xde,0,0,66,0,0};return (unsigned short*)copy(2,bytes,10);}
        API unsigned int* big32(void){unsigned char bytes[]={0,0,0,65,0,1,0xf6,0,0,0,0,66,0,0,0,0};return (unsigned int*)copy(4,bytes,16);}
        API unsigned char* fixed8(void){unsigned char bytes[]={65,0,66};return (unsigned char*)copy(1,bytes,3);}
        API unsigned char* input8(int count){(void)count;return fixed8();}
        API unsigned char* output8(int mode,int* length){*length=mode==1?-1:mode==2?9:mode==4?2:3;
            return mode==4?text8(1):fixed8();}
        API unsigned short* bytes16(int mode,int* length){unsigned short bytes[]={65,0,66};*length=mode==1?5:mode==2?-1:6;return (unsigned short*)copy(2,bytes,6);}
        API unsigned char* overflow8(int mode,unsigned long long* length){*length=mode?~0ULL:3;return fixed8();}
        API unsigned char* close8(const char* trigger){return text8(trigger[0]=='c'?0:1);}
        API unsigned char* borrow8(int missing){return missing?0:valid8;}
        API unsigned char* unknown8(void){return valid8;}
        API unsigned char* wrong_width(void){return valid8;}
        API void release8(unsigned char* value){drop(value,1);}
        API void release16(unsigned short* value){drop(value,2);}
        API void release32(unsigned int* value){drop(value,4);}
        API int releases(void){return release_count;}
        API int live(void){return live_count;}
        API int wrong_release(void){return wrong_count;}
        """;
}
