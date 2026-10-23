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

public sealed class NativeBufferValueTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ExplicitValueAndCapacityProtocolsInvokeAndForwardWithoutLeakingNativeAddresses(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-buffer-value-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "protocol.h"), """
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                typedef struct Pair { int first; int second; } Pair;
                API int transform(const int* input, int* state, int* output, Pair* pair);
                API int fill(int* buffer, int capacity, int* written, int mode);
                API int fill_bytes(int* buffer, int capacity, int* written, int mode);
                API int sum(const int* buffer, int length);
                API int alias_ref(int* first, int* second);
                API int alias_in_out(const int* input, int* output);
                API int alias_record(Pair* pair, int* field);
                API int alias_span(int* buffer, int length, int* value);
                API int fill_state(int* buffer, int capacity, int* written, int* state, int mode);
                API int alias_calls(void);
                #define FILL_ALIAS fill
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "protocol.c"), """
                #include "protocol.h"
                static int call_count;
                API int transform(const int* input, int* state, int* output, Pair* pair) {
                    *state += *input; *output = *state * 2; pair->first += *input; pair->second += 1; return *output;
                }
                API int fill(int* buffer, int capacity, int* written, int mode) {
                    ++call_count;
                    for (int i=0; i<capacity; ++i) buffer[i]=40+i;
                    *written = mode == 1 ? -1 : mode == 2 ? capacity+1 : capacity; return 42;
                }
                API int fill_bytes(int* buffer, int capacity, int* written, int mode) {
                    for (int i=0; i<capacity/(int)sizeof(int); ++i) buffer[i]=40+i;
                    *written = mode == 1 ? -1 : mode == 2 ? capacity+4 : mode == 3 ? capacity-1 : capacity; return 42;
                }
                API int sum(const int* buffer, int length) { int result=0; for(int i=0;i<length;++i) result+=buffer[i]; return result; }
                API int alias_ref(int* first, int* second) { ++call_count; *first=17; *second+=5; return (first==second?1000:0)+*first; }
                API int alias_in_out(const int* input, int* output) { ++call_count; *output=*input+3; return (input==output?1000:0)+*input; }
                API int alias_record(Pair* pair, int* field) { ++call_count; pair->first=3; *field+=7; return pair->first; }
                API int alias_span(int* buffer, int length, int* value) { ++call_count; if(length<2)return -1; buffer[1]=9; *value+=4; return (buffer+1==value?1000:0)+buffer[1]; }
                API int fill_state(int* buffer, int capacity, int* written, int* state, int mode) { ++call_count; if(capacity>0)buffer[0]=51; *state=81; *written=mode?capacity+1:capacity; return 42; }
                API int alias_calls(void) { return call_count; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "protocol", nativeName = "protocol_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["protocol.h"], includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.Protocol",
                    name = "ProtocolApi",
                    importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    names = ImmutableDictionary<string, string>.Empty.Add("FILL_ALIAS", "FillAlias"),
                    functionContainers = [new("Buffers", [new("fill", "Fill"), new("transform", "Transform")])]
                },
                semantics = new()
                {
                    valueParameters = [Value("input", NativeParameterDirection.Input), Value("state", NativeParameterDirection.InputOutput),
                        Value("output", NativeParameterDirection.Output), Value("pair", NativeParameterDirection.InputOutput),
                        Value("alias_ref", "first", NativeParameterDirection.InputOutput), Value("alias_ref", "second", NativeParameterDirection.InputOutput),
                        Value("alias_in_out", "input", NativeParameterDirection.Input), Value("alias_in_out", "output", NativeParameterDirection.Output),
                        Value("alias_record", "pair", NativeParameterDirection.InputOutput), Value("alias_record", "field", NativeParameterDirection.InputOutput),
                        Value("alias_span", "value", NativeParameterDirection.InputOutput), Value("fill_state", "state", NativeParameterDirection.InputOutput)],
                    bufferParameters = [Buffer("fill", NativeLengthUnit.Elements), Buffer("fill_bytes", NativeLengthUnit.Bytes),
                        new() { function = "sum", parameter = "buffer", lengthParameter = "length", lengthUnit = NativeLengthUnit.Elements,
                            direction = NativeParameterDirection.Input, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = true },
                        new() { function = "alias_span", parameter = "buffer", lengthParameter = "length", lengthUnit = NativeLengthUnit.Elements,
                            direction = NativeParameterDirection.InputOutput, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = true },
                        Buffer("fill_state", NativeLengthUnit.Elements)]
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.targetModels);
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string native = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            CSharpEmissionPlan plan = new HostTestEmissionPlanner().Plan(target.managed!, new(configuration.library.nativeName, new NoWrites())
            {
                importMode = mode,
                runtimeNamespace = sourceRuntime ? "Generated.Protocol.Runtime" : "BGCS.Runtime",
                includeRuntimeSource = sourceRuntime,
                managedRuntime = TestComposition.ResolveRuntime(target.resolvedTarget!)
            });
            Assert.Equal(4, plan.methods.Single(method => method.name == "Transform").valueParameters.Length);
            Assert.All(plan.methods.Where(method => method.name is "Fill" or "FillBytes"), method => Assert.True(Assert.Single(method.bufferProtocols).writtenCountIndex >= 0));
            var sources = Directory.GetFiles(Path.Combine(targetRoot, "managed"), "*.cs", SearchOption.AllDirectories)
                .Select(path => (Path: path, Text: File.ReadAllText(path))).ToArray();
            new EmittedInteropContractReader().Verify(plan, sources, configuration.library.nativeName);
            Func<SyntaxNode, bool>[] checks =
            [
                node => node is IfStatementSyntax guard && Compact(guard.Condition).StartsWith("__bgcsWritten", StringComparison.Ordinal) && Compact(guard.Condition).Contains("<0", StringComparison.Ordinal),
                node => node is IfStatementSyntax guard && Compact(guard.Condition).StartsWith("(ulong)__bgcsWritten", StringComparison.Ordinal) && Compact(guard.Condition).Contains(">(ulong)__bgcsLength", StringComparison.Ordinal),
                node => node is IfStatementSyntax guard && Compact(guard.Condition).StartsWith("(nuint)__bgcsValueAddress", StringComparison.Ordinal) && Compact(guard.Condition).Contains("!=0", StringComparison.Ordinal),
                node => node is VariableDeclaratorSyntax variable && variable.Identifier.ValueText.StartsWith("__bgcsPinnedValue", StringComparison.Ordinal)
                    && variable.Initializer is not null
            ];
            for (int check = 0; check < checks.Length; check++)
            {
                bool found = false;
                var changed = sources.Select(source =>
                {
                    SyntaxNode syntax = CSharpSyntaxTree.ParseText(source.Text, new(LanguageVersion.CSharp14)).GetRoot();
                    SyntaxNode[] matching = syntax.DescendantNodes().Where(checks[check]).ToArray();
                    found |= matching.Length != 0;
                    SyntaxNode mutation = syntax.ReplaceNodes(matching, (
                        original,
                        rewritten
                    ) => original is IfStatementSyntax
                        ? ((IfStatementSyntax)rewritten).WithCondition(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression))
                        : ((VariableDeclaratorSyntax)rewritten).WithInitializer(((VariableDeclaratorSyntax)rewritten).Initializer!.WithValue(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))));
                    return (source.Path, mutation.ToFullString());
                }).ToArray();
                Assert.True(found, "The actual generated protocol lacks expected safety/copy check " + check);
                Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                    changed, configuration.library.nativeName));
            }
            string concrete = sourceRuntime ? "ProtocolApiContext" : "ProtocolApi";
            string create = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string initialize = mode == BindingImportMode.FunctionTable ? "ProtocolApi.Load(binary);" : "ProtocolApi.Create();";
            string shared = sourceRuntime ? initialize + "\nProtocolApi.FillAlias(data, out written, 0);\nProtocolApi.Transform(in input, ref state, out output, ref pair);\nProtocolApi.Dispose();" : string.Empty;
            string consumer = $$"""
                using System;
                using System.Runtime.InteropServices;
                using Generated.Protocol;
                using ApiContext = Generated.Protocol.{{concrete}};
                public static unsafe class ProtocolConsumer {
                    public static int Run(string binary) {
                        using var api = {{create}};
                        int input=2, state=19, output;
                        Pair pair = new Pair { first=40, second=0 };
                        if(api.Transform(in input, ref state, out output, ref pair)!=42 || state!=21 || output!=42 || pair.first!=42 || pair.second!=1) return 1;
                        Span<int> data=stackalloc int[3];
                        if(api.Fill(data,out int written,0)!=42 || written!=3 || data[2]!=42) return 2;
                        if(api.FillBytes(data,out written,0)!=42 || written!=3 || data[2]!=42) return 3;
                        if(api.Sum(data)!=123 || api.Sum(ReadOnlySpan<int>.Empty)!=0) return 4;
                        if(api.Fill(Span<int>.Empty,out written,0)!=42 || written!=0) return 5;
                        if(api.FillAlias(data,out written,0)!=42 || written!=3) return 6;
                        var view=api.Buffers;
                        if(view.Fill(data,out written,0)!=42 || written!=3) return 7;
                        state=19; pair=new Pair { first=40 };
                        if(view.Transform(in input,ref state,out output,ref pair)!=42 || state!=21 || output!=42) return 8;
                        for(int mode=1;mode<=2;mode++) {
                            try { api.Fill(data,out written,mode); return 9; } catch(InvalidOperationException) { if(written!=0)return 10; }
                        }
                        for(int mode=1;mode<=3;mode++) {
                            try { api.FillBytes(data,out written,mode); return 11; } catch(InvalidOperationException) { if(written!=0)return 12; }
                        }
                        int raw=40; if(api.Raw.Sum(&raw,1)!=40)return 13;
                        int aliased=10;
                        if(api.AliasRef(ref aliased,ref aliased)!=1022 || aliased!=22)return 15;
                        aliased=10;
                        if(api.AliasInOut(in aliased,out aliased)!=1013 || aliased!=13)return 16;
                        pair=new Pair{first=5,second=11};
                        if(api.AliasRecord(ref pair,ref pair.first)!=10 || pair.first!=10 || pair.second!=11)return 17;
                        int[] overlap=[2,5,11];
                        if(api.AliasSpan(overlap,ref overlap[1])!=1013 || overlap[1]!=13)return 18;
                        int calls=api.Raw.AliasCalls();
                        byte[] bytes=new byte[12];
                        Span<int> misaligned=MemoryMarshal.Cast<byte,int>(bytes.AsSpan(1,8));
                        try{api.AliasRef(ref misaligned[0],ref misaligned[0]);return 19;}catch(ArgumentException){}
                        if(api.Raw.AliasCalls()!=calls)return 20;
                        ref int absent=ref global::System.Runtime.CompilerServices.Unsafe.NullRef<int>();
                        try{api.AliasRef(ref absent,ref absent);return 27;}catch(ArgumentException){}
                        if(api.Raw.AliasCalls()!=calls)return 28;
                        try{api.Fill(data,out absent,0);return 29;}catch(ArgumentException){}
                        if(api.Raw.AliasCalls()!=calls)return 30;
                        int[] countStorage=[7,8,9];
                        try{api.Fill(countStorage,out countStorage[0],0);return 21;}catch(ArgumentException){}
                        if(countStorage[0]!=7 || countStorage[1]!=8 || countStorage[2]!=9 || api.Raw.AliasCalls()!=calls)return 22;
                        state=7;
                        try{api.FillState(data,out state,ref state,0);return 23;}catch(ArgumentException){}
                        if(state!=7 || api.Raw.AliasCalls()!=calls)return 24;
                        try{api.FillState(data,out written,ref state,1);return 25;}catch(InvalidOperationException){}
                        if(state!=81 || written!=0)return 26;
                        {{shared}}
                        api.Dispose();
                        try { view.Fill(data,out written,0); return 14; } catch(ObjectDisposedException) { }
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), native,
                configuration.library.nativeName, consumer, "ProtocolConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ValueParameterConfiguration Value(
        string parameter,
        NativeParameterDirection direction
    ) => new()
    { function = "transform", parameter = parameter, direction = direction, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false };
    private static ValueParameterConfiguration Value(
        string function,
        string parameter,
        NativeParameterDirection direction
    ) => new()
    { function = function, parameter = parameter, direction = direction, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false };
    private static BufferParameterConfiguration Buffer(
        string function,
        NativeLengthUnit unit
    ) => new()
    {
        function = function,
        parameter = "buffer",
        capacityParameter = "capacity",
        writtenCountParameter = "written",
        lengthUnit = unit,
        direction = NativeParameterDirection.Output,
        lifetime = NativeLifetimeKind.Call,
        retainsPointer = false,
        allowsNull = true
    };
    private static string Compact(SyntaxNode node) => string.Concat(node.DescendantTokens().Select(token => token.Text));
    private sealed class NoWrites : IArtifactSink
    {
        public string WriteText(
            string relativePath,
            string content
        ) => throw new InvalidOperationException("Planning must not write artifacts.");
    }
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
