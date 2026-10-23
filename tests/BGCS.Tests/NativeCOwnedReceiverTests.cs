using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Artifacts;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Targets;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using BGCS.Interop.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCOwnedReceiverTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ExplicitCOwnersAndReceiversPreserveContextDomainsCallsAndMatchingCleanup(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-c-owned-receiver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "widget.h"), """
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                typedef struct Widget { int value; int domain; } Widget;
                API Widget* widget_create_a(const int* seed, const char* label);
                API Widget* widget_create_b(const int* seed, const char* label);
                API Widget* widget_create_fill(int* buffer, int capacity, int* written, int mode);
                API void widget_destroy_a(Widget* self);
                API void widget_destroy_b(Widget* self);
                API int widget_add(Widget* self, const int* input, int* state, int* output);
                API int widget_alias(Widget* self, const int* input, int* state, int* output);
                API int widget_fill(Widget* self, int* buffer, int capacity, int* written);
                API int widget_a_only(Widget* self);
                API int widget_get(Widget* self);
                API int widget_live(void);
                API int widget_released_a(void);
                API int widget_released_b(void);
                API int widget_wrong_release(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "widget.c"), """
                #include "widget.h"
                #include <stdlib.h>
                static int live_count, released_a, released_b, wrong_release;
                static Widget* make(const int* seed, const char* label, int domain) {
                    if (*seed < 0) return NULL;
                    Widget* result = (Widget*)malloc(sizeof(Widget));
                    if (!result) return NULL;
                    result->value = *seed + (label[0] == 'A' ? 0 : 100); result->domain = domain; ++live_count; return result;
                }
                API Widget* widget_create_a(const int* seed, const char* label) { return make(seed, label, 1); }
                API Widget* widget_create_b(const int* seed, const char* label) { return make(seed, label, 2); }
                API Widget* widget_create_fill(int* buffer, int capacity, int* written, int mode) {
                    int seed=40; Widget* result=make(&seed,"A",1);
                    for(int i=0;i<capacity;++i) buffer[i]=40+i;
                    *written=mode==1 ? -1 : mode==2 ? capacity+1 : capacity; return result;
                }
                API void widget_destroy_a(Widget* self) { if (!self) return; if(self->domain!=1) ++wrong_release; ++released_a; --live_count; free(self); }
                API void widget_destroy_b(Widget* self) { if (!self) return; if(self->domain!=2) ++wrong_release; ++released_b; --live_count; free(self); }
                API int widget_add(Widget* self, const int* input, int* state, int* output) { self->value += *input; *state += *input; *output = self->value; return self->value; }
                API int widget_alias(Widget* self, const int* input, int* state, int* output) { *state+=*input; *output=*state+self->domain; return (input==state && state==output?1000:0)+*input; }
                API int widget_fill(Widget* self, int* buffer, int capacity, int* written) { for(int i=0;i<capacity;++i) buffer[i]=self->value+i; *written=capacity; return self->value; }
                API int widget_a_only(Widget* self) { return self->domain; }
                API int widget_get(Widget* self) { return self->value; }
                API int widget_live(void) { return live_count; }
                API int widget_released_a(void) { return released_a; }
                API int widget_released_b(void) { return released_b; }
                API int widget_wrong_release(void) { return wrong_release; }
                """);
            string[] members = ["widget_add", "widget_alias", "widget_fill", "widget_a_only", "widget_get"];
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "c-owned", nativeName = "c_owned_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["widget.h"], includeTransitivelyReferencedHeaders = false, parseMacros = false },
                api = new()
                {
                    @namespace = "Generated.COwned",
                    name = "WidgetApi",
                    importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    names = ImmutableDictionary<string, string>.Empty.Add("widget_create_a", "MakeA").Add("widget_create_b", "MakeB")
                        .Add("widget_create_fill", "MakeFill")
                        .Add("widget_add", "Add").Add("widget_alias", "Alias").Add("widget_fill", "Fill").Add("widget_a_only", "AOnly").Add("widget_get", "Get"),
                    memberFunctions = members.Select(function => new ManagedReceiverRequest(function, "Widget", "self")
                    { objectName = "WidgetObject", releaseDomain = function == "widget_a_only" ? "native-a" : null }).ToImmutableArray(),
                    functionAliases = [new("widget_create_a", "MakeAlias"), new("widget_add", "AddAlias")],
                    functionContainers = [new("Widgets", [new("widget_create_a", "Make"), new("widget_add", "Add")])],
                    functions = [new("widget_create_a") { defaults = [new("label", new(ManagedDefaultValueKind.String, "A"))] }]
                },
                semantics = new()
                {
                    ownedResults = [Owned("widget_create_a", "widget_destroy_a", "native-a"), Owned("widget_create_b", "widget_destroy_b", "native-b"),
                        Owned("widget_create_fill", "widget_destroy_a", "native-a")],
                    borrowedParameters = members.Select(function => new BorrowedParameterConfiguration
                    { function = function, parameter = "self", lifetime = NativeLifetimeKind.Call, retainsPointer = false }).ToImmutableArray(),
                    valueParameters = [Value("widget_create_a", "seed", NativeParameterDirection.Input), Value("widget_create_b", "seed", NativeParameterDirection.Input),
                        Value("widget_add", "input", NativeParameterDirection.Input), Value("widget_add", "state", NativeParameterDirection.InputOutput), Value("widget_add", "output", NativeParameterDirection.Output),
                        Value("widget_alias", "input", NativeParameterDirection.Input), Value("widget_alias", "state", NativeParameterDirection.InputOutput), Value("widget_alias", "output", NativeParameterDirection.Output)],
                    stringParameters = new[] { "widget_create_a", "widget_create_b" }.Select(function => new StringParameterConfiguration
                    {
                        function = function,
                        parameter = "label",
                        encoding = NativeTextEncoding.Utf8,
                        codeUnitWidth = 1,
                        byteOrder = NativeTextByteOrder.Native,
                        lengthUnit = NativeLengthUnit.CodeUnits,
                        direction = NativeParameterDirection.Input,
                        lifetime = NativeLifetimeKind.Call,
                        zeroTerminated = true,
                        retainsPointer = false,
                        allowsNull = false
                    }).ToImmutableArray(),
                    bufferParameters = [new() { function = "widget_fill", parameter = "buffer", capacityParameter = "capacity", writtenCountParameter = "written",
                        lengthUnit = NativeLengthUnit.Elements, direction = NativeParameterDirection.Output, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = true },
                        new() { function = "widget_create_fill", parameter = "buffer", capacityParameter = "capacity", writtenCountParameter = "written",
                            lengthUnit = NativeLengthUnit.Elements, direction = NativeParameterDirection.Output, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = true }]
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string native = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            CSharpEmissionPlan plan = new HostTestEmissionPlanner().Plan(target.managed!, new(configuration.library.nativeName, new NoWrites())
            {
                importMode = mode,
                runtimeNamespace = sourceRuntime ? "Generated.COwned.Runtime" : "BGCS.Runtime",
                includeRuntimeSource = sourceRuntime,
                managedRuntime = TestComposition.ResolveRuntime(target.resolvedTarget!)
            });
            Assert.Equal(3, plan.directOwnedFactories.Length);
            Assert.Equal(5, Assert.Single(plan.objects).directMethods.Length);
            Assert.Empty(plan.ownedProjectionOmissions);
            Assert.All(target.interop!.functions, function => Assert.Null(function.sourceOwnerType));
            var sources = Directory.GetFiles(Path.Combine(targetRoot, "managed"), "*.cs", SearchOption.AllDirectories).Select(path => (Path: path, Text: File.ReadAllText(path))).ToArray();
            new EmittedInteropContractReader().Verify(plan, sources, configuration.library.nativeName);
            Func<IfStatementSyntax, bool>[] checks =
            [
                guard => guard.Condition.ToString().Contains("__BgcsBelongsTo", StringComparison.Ordinal),
                guard => guard.Condition.ToString().Contains("m_handle.releaseDomain", StringComparison.Ordinal),
                guard => guard.Condition.ToString().Contains("__bgcsOwnedPointer", StringComparison.Ordinal) && guard.Condition.ToString().Contains("==", StringComparison.Ordinal)
            ];
            foreach (Func<IfStatementSyntax, bool> check in checks)
            {
                int changed = 0;
                var mutations = sources.Select(source =>
                {
                    SyntaxNode syntax = CSharpSyntaxTree.ParseText(source.Text).GetRoot();
                    var guards = syntax.DescendantNodes().OfType<IfStatementSyntax>().Where(check).ToArray();
                    changed += guards.Length;
                    return (source.Path, syntax.ReplaceNodes(guards, (
                        _,
                        rewritten
                    ) => rewritten.WithCondition(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression))).ToFullString());
                }).ToArray();
                Assert.True(changed > 0, "Expected C ownership guard was absent.");
                Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, mutations, configuration.library.nativeName));
            }
            string concrete = sourceRuntime ? "WidgetApiContext" : "WidgetApi";
            string runtime = sourceRuntime ? "Generated.COwned.Runtime" : "BGCS.Runtime";
            string create = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary, allocator)" : "ApiContext.Create(allocator)";
            string createOther = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string sharedCreate = mode == BindingImportMode.FunctionTable ? "WidgetApi.Load(binary);" : "WidgetApi.Create();";
            string shared = sourceRuntime ? $$"""
                {{sharedCreate}}
                using(var shared = WidgetApi.MakeA(in seed)) { if(shared.Get()!=40) return 20; }
                WidgetApi.Dispose();
                """ : "";
            string consumer = $$"""
                using System;
                using System.Runtime.InteropServices;
                using Generated.COwned;
                using ApiContext = Generated.COwned.{{concrete}};
                using Runtime = {{runtime}};
                public static unsafe class COwnedConsumer {
                    public static int Run(string binary) {
                        int allocations=0, releases=0;
                        Func<Runtime.NativeModuleSession,Runtime.NativeAllocator> allocator = session => new Runtime.NativeAllocator("temporary-pair",
                            (length,alignment) => { ++allocations; return (nint)NativeMemory.Alloc(length); },
                            (address,length,alignment) => { ++releases; NativeMemory.Free((void*)address); }, Runtime.NativeAllocatorThreadDomain.AnyThread,session);
                        using var api={{create}};
                        using var other={{createOther}};
                        int seed=40,input=2,state=19,output;
                        using var first=api.MakeA(in seed);
                        using var second=api.MakeB(in seed,"A");
                        if(first.Get()!=40 || first.AOnly()!=1 || api.Get(first)!=40) return 1;
                        if(first.Add(in input,ref state,out output)!=42 || state!=21 || output!=42) return 2;
                        state=19; if(api.AddAlias(first,in input,ref state,out output)!=44 || state!=21 || output!=44) return 3;
                        state=19; if(api.Widgets.Add(first,in input,ref state,out output)!=46 || state!=21 || output!=46) return 4;
                        Span<int> values=stackalloc int[3];
                        if(first.Fill(values,out int written)!=46 || written!=3 || values[2]!=48) return 5;
                        try{other.Get(first);return 6;}catch(ArgumentException){}
                        try{second.AOnly();return 7;}catch(ArgumentException){}
                        try{api.AOnly(second);return 8;}catch(ArgumentException){}
                        seed=-1; try{api.MakeA(in seed);return 9;}catch(InvalidOperationException){} seed=40;
                        using(var alias=api.MakeAlias(in seed)) { if(alias.Get()!=40)return 10; }
                        using(var view=api.Widgets.Make(in seed)) { if(view.Get()!=40)return 11; }
                        using(var filled=api.MakeFill(values,out written,0)) { if(written!=3 || filled.Get()!=40 || values[2]!=42)return 16; }
                        for(int malformed=1;malformed<=2;++malformed) {
                            try{api.MakeFill(values,out written,malformed);return 17;}catch(InvalidOperationException){if(written!=0)return 18;}
                        }
                        if(allocations<5 || allocations!=releases)return 12;
                        int aliasValue=2;
                        if(first.Alias(in aliasValue,ref aliasValue,out aliasValue)!=1005 || aliasValue!=5)return 21;
                        aliasValue=2;
                        if(api.Alias(first,in aliasValue,ref aliasValue,out aliasValue)!=1005 || aliasValue!=5)return 22;
                        api.Dispose();
                        try{first.Get();return 13;}catch(ObjectDisposedException){}
                        first.Dispose();first.Dispose();second.Dispose();
                        if(first.releaseFailure!=null || second.releaseFailure!=null)return 14;
                        if(other.Raw.WidgetLive()!=0 || other.Raw.WidgetReleasedA()!=6 || other.Raw.WidgetReleasedB()!=1 || other.Raw.WidgetWrongRelease()!=0)return 15;
                        {{shared}}
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), native,
                configuration.library.nativeName, consumer, "COwnedConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static OwnedResultConfiguration Owned(
        string function,
        string release,
        string domain
    ) => new()
    { function = function, releaseFunction = release, releaseDomain = domain, releaseThreadDomain = "any" };
    private static ValueParameterConfiguration Value(
        string function,
        string parameter,
        NativeParameterDirection direction
    ) => new()
    { function = function, parameter = parameter, direction = direction, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false };
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
        ) => throw new InvalidOperationException("Planning must not write artifacts.");
    }
}
