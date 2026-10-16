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

public sealed class NativeBooleanValueParameterTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ExactNativeBooleanPointersPreserveAliasesWritesAndOwnedReceiverComposition(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-bool-borrow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "flags.h"), """
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                typedef _Bool NativeFlag;
                typedef struct Switch { int enabled; } Switch;
                API int flag_read(const NativeFlag* input);
                API void flag_output(NativeFlag* output);
                API void flag_change(NativeFlag* state);
                API int flag_alias(const NativeFlag* input,NativeFlag* state,NativeFlag* output);
                API void flag_write_then_count(NativeFlag* state,int* buffer,int capacity,int* written);
                API int flag_size(void);
                API int flag_alignment(void);
                API Switch* switch_create(void);
                API void switch_release(Switch* self);
                API int switch_set(Switch* self,NativeFlag* enabled);
                API int switch_live(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "flags.c"), """
                #include "flags.h"
                #include <stdlib.h>
                static int live;
                API int flag_read(const NativeFlag* input){return *input ? 1 : 0;}
                API void flag_output(NativeFlag* output){*output=1;}
                API void flag_change(NativeFlag* state){*state=!*state;}
                API int flag_alias(const NativeFlag* input,NativeFlag* state,NativeFlag* output){
                    int equal=input==state && state==output;
                    *state=!*input;*output=!*state;
                    return (equal?100:0)+(*input?1:0);
                }
                API void flag_write_then_count(NativeFlag* state,int* buffer,int capacity,int* written){
                    *state=1;for(int i=0;i<capacity;++i)buffer[i]=i;*written=capacity+1;
                }
                API int flag_size(void){return sizeof(NativeFlag);}
                API int flag_alignment(void){return _Alignof(NativeFlag);}
                API Switch* switch_create(void){Switch* value=(Switch*)malloc(sizeof(Switch));if(value){value->enabled=0;++live;}return value;}
                API void switch_release(Switch* self){if(self){--live;free(self);}}
                API int switch_set(Switch* self,NativeFlag* enabled){self->enabled=*enabled;*enabled=!*enabled;return self->enabled;}
                API int switch_live(void){return live;}
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "boolean-borrow", nativeName = "boolean_borrow_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["flags.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.NativeBooleanBorrow", name = "FlagApi", importMode = mode,
                    names = ImmutableDictionary<string, string>.Empty.Add("flag_read", "Read").Add("flag_output", "Output").Add("flag_change", "Change")
                        .Add("flag_alias", "Alias").Add("flag_write_then_count", "WriteThenCount").Add("flag_size", "Size").Add("flag_alignment", "Alignment")
                        .Add("switch_create", "Make").Add("switch_set", "Set").Add("switch_live", "Live"),
                    functionAliases = [new("flag_alias", "AliasForward")], functionContainers = [new("Flags", [new("flag_alias", "Apply")])],
                    memberFunctions = [new("switch_set", "Switch", "self") { objectName = "SwitchObject", releaseDomain = "native-switch" }]
                },
                semantics = new()
                {
                    valueParameters = [Value("flag_read", "input", NativeParameterDirection.Input), Value("flag_output", "output", NativeParameterDirection.Output),
                        Value("flag_change", "state", NativeParameterDirection.InputOutput), Value("flag_alias", "input", NativeParameterDirection.Input),
                        Value("flag_alias", "state", NativeParameterDirection.InputOutput), Value("flag_alias", "output", NativeParameterDirection.Output),
                        Value("flag_write_then_count", "state", NativeParameterDirection.InputOutput), Value("switch_set", "enabled", NativeParameterDirection.InputOutput)],
                    bufferParameters = [new() { function = "flag_write_then_count", parameter = "buffer", capacityParameter = "capacity", writtenCountParameter = "written",
                        direction = NativeParameterDirection.Output, lengthUnit = NativeLengthUnit.Elements, lifetime = NativeLifetimeKind.Call, allowsNull = true, retainsPointer = false }],
                    ownedResults = [new() { function = "switch_create", releaseFunction = "switch_release", releaseDomain = "native-switch", releaseThreadDomain = "any" }],
                    borrowedParameters = [new() { function = "switch_set", parameter = "self", lifetime = NativeLifetimeKind.Call, retainsPointer = false }]
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" }, output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                nativeBuild = new() { mode = "sources", sources = ["flags.c"] }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.generation.targetModels);
            var boolean = Assert.Single(target.interop.types, type => type.primitiveKind == "Bool");
            Assert.Equal(1, boolean.size);
            Assert.Equal(1, boolean.alignment);
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string factory = mode == BindingImportMode.FunctionTable ? "FlagApi.Load(binary)" : "FlagApi.Create()";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"),
                Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin"))), configuration.library.nativeName, $$"""
                using System;
                using Generated.NativeBooleanBorrow;
                public static unsafe class FlagConsumer {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        if(api.Raw.Size()!=sizeof(bool) || api.Raw.Size()!=1 || api.Raw.Alignment()!=1)return 1;
                        bool flag=false;
                        if(api.Read(in flag)!=0)return 2;
                        api.Output(out flag);if(!flag)return 3;
                        api.Change(ref flag);if(flag)return 4;
                        flag=true;
                        if(api.Alias(in flag,ref flag,out flag)!=101 || !flag)return 5;
                        if(api.AliasForward(in flag,ref flag,out flag)!=101 || !flag)return 6;
                        if(api.Flags.Apply(in flag,ref flag,out flag)!=101 || !flag)return 7;
                        bool[] managed=[true];
                        if(api.Alias(in managed[0],ref managed[0],out managed[0])!=101 || !managed[0])return 14;
                        api.Change(ref managed[0]);if(managed[0])return 15;
                        api.Output(out managed[0]);if(!managed[0])return 16;
                        flag=false;
                        bool caught=false;try{api.WriteThenCount(ref flag,new int[1],out int count);}catch(InvalidOperationException){caught=true;}
                        if(!caught || !flag)return 8;
                        byte raw=1;api.Raw.Change(&raw);if(raw!=0)return 9;
                        using(var item=api.Make()){
                            if(api.Raw.Live()!=1)return 10;
                            flag=true;if(item.Set(ref flag)!=1 || flag)return 11;
                            if(item.Set(ref flag)!=0 || !flag)return 12;
                            if(item.Set(ref managed[0])!=1 || managed[0])return 17;
                        }
                        if(api.Raw.Live()!=0)return 13;
                        return 42;
                    }
                }
                """, "FlagConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ValueParameterConfiguration Value(string function, string parameter, NativeParameterDirection direction) => new()
    { function = function, parameter = parameter, direction = direction, lifetime = NativeLifetimeKind.Call, allowsNull = false, retainsPointer = false };

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
