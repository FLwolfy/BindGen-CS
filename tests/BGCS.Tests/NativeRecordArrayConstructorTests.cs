using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeRecordArrayConstructorTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task IndependentArrayCopiesCoexistWithOwnedBooleanMembersDelegateOptOutAndTypedDefaults(BindingImportMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-array-presentation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "thing.h"), """
                #include <stddef.h>
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                typedef void (*Notify)(int value);
                typedef struct NativeThing { unsigned char data[1]; Notify notify; int value; } NativeThing;
                API NativeThing* NativeThing_NativeThing(int value);
                API void NativeThing_Release(NativeThing* self);
                API void NativeThing_Reset(NativeThing* self);
                API void NativeThing_Resize(NativeThing* self,int size);
                API int NativeThing_SetEnabled(NativeThing* self,_Bool* enabled);
                API int NativeThing_Value(NativeThing* self);
                API int NativeFormat(const char* label,const char* format);
                API int NativeFlags(unsigned int flags);
                API int NativeBegin(const char* label,unsigned char* p_open,unsigned int flags);
                API int data_verify(const NativeThing* value);
                API size_t data_offset(void);
                API size_t thing_size(void);
                API int things_live(void);
                API int things_released(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "thing.c"), """
                #include "thing.h"
                #include <stdlib.h>
                #include <string.h>
                static int live,released;
                API NativeThing* NativeThing_NativeThing(int value){NativeThing* result=(NativeThing*)malloc(sizeof(NativeThing));
                    if(result){memset(result,0,sizeof(*result));result->data[0]=(unsigned char)value;result->value=value;++live;}return result;}
                API void NativeThing_Release(NativeThing* self){if(self){--live;++released;free(self);}}
                API void NativeThing_Reset(NativeThing* self){self->value=0;}
                API void NativeThing_Resize(NativeThing* self,int size){self->value=size;}
                API int NativeThing_SetEnabled(NativeThing* self,_Bool* enabled){self->value=*enabled?42:0;*enabled=!*enabled;return self->value;}
                API int NativeThing_Value(NativeThing* self){return self->value;}
                API int NativeFormat(const char* label,const char* format){return strcmp(label,"AB")==0 && strcmp(format,"%.3f")==0?42:-1;}
                API int NativeFlags(unsigned int flags){return flags==0?42:-1;}
                API int NativeBegin(const char* label,unsigned char* p_open,unsigned int flags){return strcmp(label,"AB")==0 && !p_open && flags==0?42:-1;}
                API int data_verify(const NativeThing* value){return value->data[0]==42 && value->notify==0 && value->value==0?42:-1;}
                API size_t data_offset(void){return offsetof(NativeThing,data);}
                API size_t thing_size(void){return sizeof(NativeThing);}
                API int things_live(void){return live;}
                API int things_released(void){return released;}
                """);
            string[] members = ["NativeThing_Reset", "NativeThing_Resize", "NativeThing_SetEnabled", "NativeThing_Value"];
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "array-presentation", nativeName = "array_presentation_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["thing.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.ArrayPresentation", name = "ThingApi", importMode = mode, emitCallbackDelegates = false,
                    style = mode == BindingImportMode.LibraryImport ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    names = ImmutableDictionary<string, string>.Empty.Add("NativeThing_NativeThing", "Make").Add("NativeThing_Reset", "Reset")
                        .Add("NativeThing_Resize", "Resize").Add("NativeThing_SetEnabled", "SetEnabled").Add("NativeThing_Value", "Value")
                        .Add("NativeFormat", "Format").Add("NativeFlags", "Flags").Add("NativeBegin", "Begin"),
                    memberFunctions = members.Select(function => new ManagedReceiverRequest(function, "NativeThing", "self") { objectName = "ThingObject" }).ToImmutableArray(),
                    records = [new("NativeThing") { constructorFields = ["data"] }],
                    functionAliases = [new("NativeBegin", "BeginAlias")], functionContainers = [new("Begins", [new("NativeBegin", "Open")])],
                    functions = [new("NativeFormat") { defaults = [new("format", new(ManagedDefaultValueKind.String, "%.3f"))] },
                        new("NativeFlags") { defaults = [new("flags", new(ManagedDefaultValueKind.Integer, "0"))] },
                        new("NativeBegin") { defaults = [new("p_open", new(ManagedDefaultValueKind.Null)), new("flags", new(ManagedDefaultValueKind.Integer, "0"))] }]
                },
                semantics = new()
                {
                    ownedResults = [new() { function = "NativeThing_NativeThing", releaseFunction = "NativeThing_Release", releaseDomain = "native-thing", releaseThreadDomain = "any" }],
                    borrowedParameters = members.Select(function => new BorrowedParameterConfiguration { function = function, parameter = "self", lifetime = NativeLifetimeKind.Call, retainsPointer = false }).ToImmutableArray(),
                    valueParameters = [new() { function = "NativeThing_SetEnabled", parameter = "enabled", direction = NativeParameterDirection.InputOutput,
                        lifetime = NativeLifetimeKind.Call, allowsNull = false, retainsPointer = false },
                        new() { function = "NativeBegin", parameter = "p_open", direction = NativeParameterDirection.InputOutput,
                            lifetime = NativeLifetimeKind.Call, allowsNull = true, retainsPointer = false }],
                    stringParameters = new[] { "label", "format" }.Select(parameter => new StringParameterConfiguration { function = "NativeFormat", parameter = parameter,
                        encoding = NativeTextEncoding.Utf8, codeUnitWidth = 1, byteOrder = NativeTextByteOrder.Native,
                        direction = NativeParameterDirection.Input, lengthUnit = NativeLengthUnit.CodeUnits, lifetime = NativeLifetimeKind.Call,
                        zeroTerminated = true, retainsPointer = false, allowsNull = false }).Append(new StringParameterConfiguration
                        { function = "NativeBegin", parameter = "label", encoding = NativeTextEncoding.Utf8, codeUnitWidth = 1, byteOrder = NativeTextByteOrder.Native,
                            direction = NativeParameterDirection.Input, lengthUnit = NativeLengthUnit.CodeUnits, lifetime = NativeLifetimeKind.Call,
                            zeroTerminated = true, retainsPointer = false, allowsNull = false }).ToImmutableArray()
                },
                 cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.targetModels);
            CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(target.managed!, new(configuration.library.nativeName, new NoWrites()) { importMode = mode });
            CSharpStoragePlan storage = Assert.Single(plan.storage, carrier => carrier.name == "NativeThing");
            CSharpRecordArrayCopyPlan array = Assert.Single(storage.presentation!.arrayCopies);
            Assert.Equal(1, array.length);
            Assert.Equal("byte", array.elementType);
            Assert.Empty(plan.abiDelegates);
            CSharpNullDefaultMethodPlan nullDefault = Assert.Single(plan.nullDefaultMethods);
            Assert.Equal("Begin", nullDefault.signature.name);
            Assert.Equal(new[] { "string", "uint" }, nullDefault.signature.parameterTypes);
            Assert.Equal(4, Assert.Single(plan.objects).directMethods.Length);
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            using (JsonDocument metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(targetRoot, "metadata", "abi.manifest.json"))))
            {
                JsonElement operation = Assert.Single(metadata.RootElement.GetProperty("managedNullDefaultMethods").EnumerateArray());
                Assert.Equal(nullDefault.methodIndex, operation.GetProperty("methodIndex").GetInt32());
                JsonElement binding = Assert.Single(operation.GetProperty("nullInputs").EnumerateArray());
                CSharpNullDefaultInputPlan expected = Assert.Single(nullDefault.nullInputs);
                Assert.Equal(expected.parameterIndex, binding.GetProperty("parameterIndex").GetInt32());
                Assert.Equal(expected.nativeParameter.value, binding.GetProperty("nativeParameter").GetProperty("value").GetString());
                Assert.Equal(expected.rawPointerType, binding.GetProperty("rawPointerType").GetString());
            }
            var sources = Directory.GetFiles(Path.Combine(targetRoot, "managed"), "*.cs", SearchOption.AllDirectories).Select(path => (Path: path, Text: File.ReadAllText(path))).ToArray();
            new EmittedInteropContractReader().Verify(plan, sources, configuration.library.nativeName);
            Assert.DoesNotContain(sources.SelectMany(source => CSharpSyntaxTree.ParseText(source.Text).GetRoot().DescendantNodes().OfType<DelegateDeclarationSyntax>()), declaration => declaration.Identifier.ValueText == "Notify");
            CSharpFieldPlan rawField = Assert.Single(storage.fields, field => field.nativeField == array.nativeField);
            string rawLabelType = plan.methods.Single(method => method.name == "Begin").parameterTypes[0];
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string shared = mode == BindingImportMode.LibraryImport ? "ThingApi.Create();if(ThingApi.Begin(\"AB\")!=42 || ThingApi.BeginAlias(\"AB\")!=42 || ThingApi.Begins.Open(\"AB\")!=42)return 15;ThingApi.Dispose();" : string.Empty;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"),
                Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot))), configuration.library.nativeName, $$"""
                using System;
                using System.Runtime.InteropServices;
                using Generated.ArrayPresentation;
                using ApiContext=Generated.ArrayPresentation.{{plan.contextName}};
                public static unsafe class ArrayPresentationConsumer {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        byte[] input=[42];NativeThing copied=new(input);input[0]=99;
                        if(copied.data[0]!=42 || api.Raw.DataVerify(&copied)!=42 || api.Raw.ThingSize()!=(nuint)sizeof(NativeThing))return 1;
                        if(api.Raw.DataOffset()!=(nuint)Marshal.OffsetOf<NativeThing>("data"))return 2;
                        {{rawField.type}} rawData=default;rawData[0]=42;NativeThing raw=new(rawData);rawData[0]=7;
                        if(raw.data[0]!=42 || api.Raw.DataVerify(&raw)!=42)return 3;
                        bool rejected=false;try{_=new NativeThing(Array.Empty<byte>());}catch(ArgumentException){rejected=true;}
                        if(!rejected)return 4;
                        rejected=false;try{_=new NativeThing(new byte[2]);}catch(ArgumentException){rejected=true;}
                        if(!rejected)return 5;
                        using(var item=api.Make(9)) {
                            if(item.Value()!=9 || api.Raw.ThingsLive()!=1)return 6;
                            item.Reset();if(item.Value()!=0)return 7;
                            item.Resize(11);if(item.Value()!=11)return 8;
                            bool enabled=true;if(item.SetEnabled(ref enabled)!=42 || enabled)return 9;
                        }
                        if(api.Raw.ThingsLive()!=0 || api.Raw.ThingsReleased()!=1)return 10;
                        if(api.Format("AB")!=42 || api.Flags()!=42)return 11;
                        if(api.Begin("AB",0)!=42 || api.Begin("AB")!=42 || api.BeginAlias("AB")!=42 || api.Begins.Open("AB")!=42)return 13;
                        // The complete typed pointer operation remains separately callable in Raw.
                        fixed(byte* label="AB"u8)if(api.Raw.Begin(({{rawLabelType}})label,null,0)!=42)return 12;
                        {{shared}}
                        return 42;
                    }
                }
                """, "ArrayPresentationConsumer", root, sourceRuntime: false));
            if (mode == BindingImportMode.FunctionTable)
            {
                var before = Snapshot(built.outputRoot);
                BindingConfiguration refusedConfiguration = configuration with { semantics = configuration.semantics with
                { valueParameters = configuration.semantics.valueParameters.Select(rule => rule.function == "NativeBegin" ? rule with { allowsNull = false } : rule).ToImmutableArray() } };
                GenerationResult refused = await host.GenerateAsync(new(refusedConfiguration, root));
                Assert.False(refused.success);
                Assert.False(refused.published);
                Assert.Equal(before.OrderBy(item => item.Key), Snapshot(built.outputRoot).OrderBy(item => item.Key));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static System.Collections.Generic.Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private sealed class NoWrites : IArtifactSink
    {
        public string WriteText(string relativePath, string content) => throw new InvalidOperationException("Planning cannot write a candidate.");
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
