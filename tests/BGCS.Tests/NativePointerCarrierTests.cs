using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativePointerCarrierTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ExplicitAddressesPreserveOpaqueAliasesNestedArraysConstAndNativeIndirection(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-pointer-carrier-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "addresses.h"), """
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                typedef struct NativeWindowS NativeWindow;
                typedef void* NativeContext;
                typedef union NativeChoice { int integer; float number; } NativeChoice;
                typedef struct NativeNode NativeNode;
                struct NativeNode { int value; int values[2]; NativeChoice choices[2]; NativeNode* next; const int tag; };
                API NativeNode* address_node(void);
                API const NativeNode* address_const_node(void);
                API NativeNode** address_nodes(void);
                API NativeWindow* address_window(void);
                API NativeWindow** address_windows(void);
                API int address_read(NativeNode* self,const char* text);
                API void address_read_after(const char* text, NativeNode* self, int* result);
                API int address_read_const(const NativeNode* self);
                API int address_window_value(NativeWindow* window);
                API NativeContext address_context(void);
                API int address_context_value(NativeContext context);
                API int address_check(NativeNode** nodes,NativeWindow** windows);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "addresses.c"), """
                #include "addresses.h"
                #include <string.h>
                struct NativeWindowS { int value; };
                static NativeWindow window={42};
                static NativeWindow* windows[2]={&window,0};
                static NativeNode second={3,{4,5},{{6},{7}},0,8};
                static NativeNode first={40,{1,2},{{3},{4}},&second,9};
                static NativeNode* nodes[2]={&first,&second};
                API NativeNode* address_node(void){return &first;}
                API const NativeNode* address_const_node(void){return &first;}
                API NativeNode** address_nodes(void){return nodes;}
                API NativeWindow* address_window(void){return &window;}
                API NativeWindow** address_windows(void){return windows;}
                API int address_read(NativeNode* self,const char* text){return self->value+(strcmp(text,"AB")==0?2:-100);}
                API void address_read_after(const char* text, NativeNode* self, int* result){*result=address_read(self,text);}
                API int address_read_const(const NativeNode* self){return self->value+self->tag;}
                API int address_window_value(NativeWindow* value){return value==&window?value->value:-1;}
                API NativeContext address_context(void){return &window;}
                API int address_context_value(NativeContext context){return context==&window?42:-1;}
                API int address_check(NativeNode** a,NativeWindow** b){return a==nodes && b==windows && a[0]==&first && a[1]==&second && b[0]==&window?42:-1;}
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "addresses", nativeName = "addresses_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["addresses.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.Addresses", name = "AddressApi", importMode = mode, style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    nestGeneratedTypesInApi = sourceRuntime,
                    names = ImmutableDictionary<string, string>.Empty.Add("address_node", "Node").Add("address_const_node", "ConstNode").Add("address_nodes", "Nodes")
                        .Add("address_window", "Window").Add("address_windows", "Windows").Add("address_read", "Read").Add("address_read_const", "ReadConst")
                        .Add("address_window_value", "WindowValue").Add("address_check", "Check"),
                    pointerCarriers = [new("NativeNode", "NodeAddress"), new("NativeNode", "ConstNodeAddress") { readOnly = true },
                        new("NativeNode", "NodeListAddress") { pointerDepth = 2 }, new("NativeWindow", "WindowAddress"), new("NativeWindow", "WindowListAddress") { pointerDepth = 2 },
                        new("NativeContext", "ContextAddress")],
                    pointerFunctions = [new("address_node") { returns = "NodeAddress" }, new("address_const_node") { returns = "ConstNodeAddress" },
                        new("address_nodes") { returns = "NodeListAddress" }, new("address_window") { returns = "WindowAddress" }, new("address_windows") { returns = "WindowListAddress" },
                        new("address_read") { parameters = [new("self", "NodeAddress")], receiver = "self" }, new("address_read_const") { parameters = [new("self", "ConstNodeAddress")], receiver = "self" },
                        new("address_read_after") { name = "ReadAfter", parameters = [new("self", "NodeAddress")], receiver = "self" },
                        new("address_window_value") { parameters = [new("window", "WindowAddress")] }, new("address_check") { parameters = [new("nodes", "NodeListAddress"), new("windows", "WindowListAddress")] },
                        new("address_context") { name = "GetContextAddress", returns = "ContextAddress" }, new("address_context_value") { name = "ContextValue", parameters = [new("context", "ContextAddress")] }],
                    functionAliases = [new("address_read", "ReadAlias")]
                },
                semantics = new() { stringParameters = [new() { function = "address_read", parameter = "text", encoding = NativeTextEncoding.Utf8, codeUnitWidth = 1,
                    direction = NativeParameterDirection.Input, lengthUnit = NativeLengthUnit.CodeUnits, lifetime = NativeLifetimeKind.Call,
                    zeroTerminated = true, retainsPointer = false, allowsNull = false },
                    new() { function = "address_read_after", parameter = "text", encoding = NativeTextEncoding.Utf8, codeUnitWidth = 1,
                    direction = NativeParameterDirection.Input, lengthUnit = NativeLengthUnit.CodeUnits, lifetime = NativeLifetimeKind.Call,
                    zeroTerminated = true, retainsPointer = false, allowsNull = false }],
                    valueParameters = [new() { function = "address_read_after", parameter = "result", direction = NativeParameterDirection.Output,
                        lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false }] },
                 runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.targetModels);
            Assert.All(target.interop!.functions, function => Assert.Null(function.returnOwnership));
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            string context = sourceRuntime ? "AddressApiContext" : "AddressApi";
            string prefix = sourceRuntime ? "AddressApi." : string.Empty;
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string shared = sourceRuntime ? (mode == BindingImportMode.FunctionTable ? "AddressApi.Load(binary);" : "AddressApi.Create();")
                + "if(AddressApi.Read(AddressApi.Node(),\"AB\")!=42)return 21;AddressApi.Dispose();" : "";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, configuration.library.nativeName, $$"""
                using System;
                using Generated.Addresses;
                using ApiContext=Generated.Addresses.{{context}};
                using NodeAddress=Generated.Addresses.{{prefix}}NodeAddress;
                public static unsafe class AddressConsumer {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        using var other={{factory}};
                        var node=api.Node();var constant=api.ConstNode();var list=api.Nodes();var window=api.Window();var windows=api.Windows();
                        if(node.IsNull || window.IsNull || NodeAddress.Null!=default || api.WindowValue(window)!=42)return 1;
                        if(api.ContextValue(api.GetContextAddress())!=42 || api.GetContextAddress().Handle!=window.Handle)return 15;
                        if(api.Read(node,"AB")!=42 || api.ReadAlias(node,"AB")!=42 || other.Read(node,"AB")!=42 || node.Read(api,"AB")!=42 || node.Read(other,"AB")!=42)return 2;
                        try{node.Read(null!,"AB");return 22;}catch(ArgumentNullException){}
                        node.ReadAfter(api,"AB",out int result);if(result!=42)return 24;
                        if(node.Handle!=(nint)api.Raw.Node() || constant.Handle!=node.Handle || api.Check(list,windows)!=42)return 3;
                        if(list.DangerousGetAddressElement(0)!=node || windows.DangerousGetAddressElement(0)!=window)return 4;
                        var next=node.DangerousField_next;if(next.Handle==0 || next.DangerousField_value()!=3)return 5;
                        node.DangerousField_next=node;
                        if(node.DangerousField_next!=node)return 6;
                        node.DangerousField_next=next;
                        var values=node.DangerousArray_values();values[0]=19;values[1]=23;
                        if(constant.DangerousArray_values()[0]+constant.DangerousArray_values()[1]!=42)return 7;
                        var choices=node.DangerousArray_choices();choices[0].integer=17;choices[1].integer=25;
                        if(constant.DangerousArray_choices()[0].integer+constant.DangerousArray_choices()[1].integer!=42)return 8;
                        node.DangerousField_value()=41;if(api.ReadConst(constant)!=50 || constant.ReadConst(api)!=50 || constant.DangerousField_tag()!=9)return 9;
                        node.DangerousField_value()=40;
                        list.DangerousSetAddressElement(1,node);if(list.DangerousGetAddressElement(1)!=node)return 10;list.DangerousSetAddressElement(1,next);
                        if(api.Check(list,windows)!=42 || node.GetHashCode()!=new NodeAddress(node.Handle).GetHashCode() || !node.Equals((object)new NodeAddress(node.Handle)))return 11;
                        try{NodeAddress.Null.DangerousField_value();return 12;}catch(InvalidOperationException){}
                        try{new NodeAddress(node.Handle+1).DangerousField_value();return 13;}catch(InvalidOperationException){}
                        {{shared}}
                        api.Dispose();try{api.Read(node,"AB");return 14;}catch(ObjectDisposedException){}
                        try{node.Read(api,"AB");return 23;}catch(ObjectDisposedException){}
                        return 42;
                    }
                }
                """, "AddressConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
