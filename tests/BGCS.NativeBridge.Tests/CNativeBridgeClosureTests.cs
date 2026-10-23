using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Interop;
using BGCS.Core.Artifacts;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.NativeBridge.Emission;
using BGCS.NativeBridge.Planning;
using BGCS.NativeBridge.Recipes;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class CNativeBridgeClosureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedAndAsyncContextsPreserveIndependentUserDataAndCancelOnlyBorrows(bool asynchronous)
    {
        LoweringContext input = CallbackInput(asynchronous);
        NativeBridgePlan plan = new NativeBridgePlanner().Plan(input);
        NativeCallbackContextPlan context = Assert.Single(plan.callbackContexts);
        Assert.Equal(asynchronous, context.cancelFunction is not null);
        Assert.Equal(2, context.callbackParameters.Length);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(plan, sink, "fixture");
        Assert.DoesNotContain("= {0}", sink.files["src/fixture.c"], StringComparison.Ordinal);
        Assert.Contains("initialize the complete result on every invocation", sink.files["src/fixture.c"], StringComparison.Ordinal);
        string register = plan.exports.Single(export => export.operation.declaration!.Value.value == "register").name;
        string stop = plan.exports.Single(export => export.operation.declaration!.Value.value == "stop").name;
        string cancel = plan.exports.Single(export => export.operation.declaration!.Value.value == "cancel").name;
        string handle = plan.types.Single(type => type.id == context.carrierType).cName;
        string cancellation = asynchronous ? cancel + "(first); if (native_invoke_all() != 34) return 4;" : "";
        string consumer = $$"""
            #include "include/fixture.h"
            static void transform(const Sample* value, void* data, Sample* result) { result->value = value->value + *(int*)data; }
            static void notice(const Sample* value, void* data) { *(int*)data += value->value; }
            int main(void) {
                int a = 10, b = 20;
                {{handle}} first = {{register}}(transform, notice, &a);
                {{handle}} second = {{register}}(transform, notice, &b);
                if (!first || !second || native_invoke_all() != 32) return 1;
                if (a != 11 || b != 21) return 2;
                {{cancellation}}
                {{stop}}(first);
                if (native_invoke_all() != {{(asynchronous ? 23 : 22)}}) return 5;
                {{stop}}(second);
                if (native_invoke_all() != 0) return 6;
                native_fail_next();
                if ({{register}}(transform, notice, &a) != NULL) return 7;
                if (native_invoke_all() != 0) return 8;
                return 0;
            }
            """;
        await CompileAndRun(sink, CallbackHeader, CallbackSource, consumer);
    }

    [Theory]
    [InlineData("no-native-frame-drain")]
    [InlineData("null-result-active")]
    [InlineData("missing-stop")]
    public void RetainedContextCannotInventShutdownGuarantees(string fault)
    {
        LoweringContext input = CallbackInput(false);
        InteropModule semantics = input.semantics!;
        InteropFunction registration = semantics.functions.Single(function => function.id.value == "register");
        registration = registration with
        {
            parameters = registration.parameters.Select(parameter => parameter.callback is null ? parameter : parameter with
            {
                callback = fault switch
                {
                    "no-native-frame-drain" => parameter.callback! with { unregisterQuiescence = NativeQuiescenceKind.NoNewInvocations },
                    "null-result-active" => parameter.callback! with { registration = parameter.callback!.registration! with { nullResultQuiescence = NativeQuiescenceKind.NoNewInvocations } },
                    _ => parameter.callback! with { unregisterFunction = new("not-selected") }
                }
            }).ToImmutableArray()
        };
        Assert.Throws<NotSupportedException>(() => new NativeBridgePlanner().Plan(input with
        { semantics = WithFunctions(semantics, semantics.functions.Replace(semantics.functions.Single(function => function.id.value == "register"), registration)) }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomCTypeAndCallableConversionsProduceRealInvocations(bool callable)
    {
        LoweringContext input = ValueInput();
        LoweringRegistry registry = callable
            ? new(callables: [(new("fixture.callable", ExtensionPort.NativeCallableLowering), new CallableLowering())])
            : new(types: [(new("fixture.type", ExtensionPort.NativeTypeLowering), new ValueLowering())]);
        NativeBridgePlan plan = new NativeBridgePlanner(registry).Plan(input);
        BridgeExport export = Assert.Single(plan.exports);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(plan, sink, "fixture");
        Assert.Equal(1, sink.files["src/fixture.c"].Split("native_echo(", StringSplitOptions.None).Length - 1);
        string arguments = callable ? "7" : "10, 20";
        int expected = callable ? 24 : 61;
        await CompileAndRun(sink, ValueHeader, "#include \"native.h\"\nSample native_echo(Sample value) { value.value *= 2; return value; }", $$"""
            #include "include/fixture.h"
            int main(void) { return {{export.name}}({{arguments}}) == {{expected}} ? 0 : 1; }
            """);
    }

    [Fact]
    public async Task FiniteCVariadicWrappersUseRealArgumentFactsAndPreserveNativePromotions()
    {
        var basic = Basics();
        NativeType small = new(new("short"), NativeTypeCategory.Primitive, "short", 2, 2) { primitiveKind = "Short", isSigned = true };
        NativeType single = new(new("float"), NativeTypeCategory.Primitive, "float", 4, 4) { primitiveKind = "Float" };
        NativeDeclaration function = Function("sum", "Sum", new("int"), new StableId("count")) with { isVariadic = true };
        NativeDeclaration prefix = Parameter("count", "count", function.id, new("int"));
        NativeModule module = new("fixture-x64", "x86_64-fixture", 8, basic.Types.Add(small).Add(single), basic.Declarations.Add(function).Add(prefix), [])
        { language = SourceLanguage.C, byteOrder = NativeByteOrder.LittleEndian };
        NativeVariadicAdapter adapter = new(function.id, new BGCS.Analysis.Types.NativeTypeFormatter().CallableSignature(module, function), "SumShortFloat", 1,
            [new("first", "short"), new("second", "float")])
        { argumentTypes = [small.id, single.id] };
        NativeBridgePlan plan = new NativeBridgePlanner().Plan(Context(module), variadicAdapters: [adapter]) with
        { postamble = "static inline int SumShortFloat(int count, short first, float second) { return Sum(count, first, second); }" };
        BridgeExport export = Assert.Single(plan.exports);
        Assert.Equal(BridgeOperationKind.FiniteVariadicCall, export.operation.kind);
        Assert.Equal(function.id, export.operation.declaration);
        Assert.Equal(3, export.parameters.Length);
        Assert.DoesNotContain(plan.source.declarations, declaration => declaration.name == "SumShortFloat");
        Sink sink = new(); new TypedBridgeEmitter().Emit(plan, sink, "fixture");
        await CompileAndRun(sink, "#pragma once\nint Sum(int count, ...);\n", """
            #include "native.h"
            #include <stdarg.h>
            int Sum(int count, ...) {
                va_list args; va_start(args, count);
                int first = va_arg(args, int); double second = va_arg(args, double);
                va_end(args); return count + first + (int)second;
            }
            """, $$"""
            #include "include/fixture.h"
            int main(void) { return {{export.name}}(30, (short)7, 5.5f) == 42 ? 0 : 1; }
            """);
    }

    [Fact]
    public void ChangedCustomSourceBytesAreRejectedBeforeAnyArtifactIsWritten()
    {
        NativeBridgePlan plan = new NativeBridgePlanner(new(types: [(new("fixture.type", ExtensionPort.NativeTypeLowering), new ValueLowering())])).Plan(ValueInput());
        BridgeExport export = Assert.Single(plan.exports);
        BridgeArgumentGroup group = Assert.Single(export.operation.argumentGroups);
        plan = plan with
        {
            exports = [export with { operation = export.operation with { argumentGroups = [group with
        { expression = group.expression! with { text = group.expression!.text + " + 1" } }] } }]
        };
        Sink sink = new();
        Assert.Throws<InvalidOperationException>(() => new TypedBridgeEmitter().Emit(plan, sink, "fixture"));
        Assert.Empty(sink.files);
    }

    private static LoweringContext ValueInput()
    {
        var basic = Basics();
        NativeDeclaration function = Function("echo", "native_echo", new("sample"), new StableId("echo-input"));
        NativeDeclaration parameter = Parameter("echo-input", "value", function.id, new("sample"));
        NativeModule module = new("fixture-x64", "x86_64-fixture", 8, basic.Types, basic.Declarations.AddRange((ImmutableArray<NativeDeclaration>)[function, parameter]), [])
        { language = SourceLanguage.C, byteOrder = NativeByteOrder.LittleEndian };
        return Context(module);
    }

    private static LoweringContext CallbackInput(bool asynchronous)
    {
        var basic = Basics();
        NativeType state = new(new("state"), NativeTypeCategory.Pointer, "void*", 8, 8) { elementType = new("void") };
        NativeType nativeHandle = new(new("handle"), NativeTypeCategory.Pointer, "NativeRegistration*", 8, 8) { elementType = new("registration") };
        NativeType registration = new(new("registration"), NativeTypeCategory.Record, "NativeRegistration", null, null) { declaration = new("registration-declaration") };
        NativeType transform = new(new("transform"), NativeTypeCategory.Function, "Sample(Sample,void*)", null, null)
        { returnType = new("sample"), parameterTypes = [new("sample"), state.id], callingConvention = "C" };
        NativeType notice = new(new("notice"), NativeTypeCategory.Function, "void(Sample,void*)", null, null)
        { returnType = new("void"), parameterTypes = [new("sample"), state.id], callingConvention = "C" };
        NativeType transformPointer = new(new("transform-pointer"), NativeTypeCategory.Pointer, "Transform", 8, 8) { elementType = transform.id };
        NativeType noticePointer = new(new("notice-pointer"), NativeTypeCategory.Pointer, "Notice", 8, 8) { elementType = notice.id };
        NativeDeclaration register = Function("register", "native_register", nativeHandle.id, new("transform-input"), new("notice-input"), new("data-input"));
        NativeDeclaration stop = Function("stop", "native_stop", new("void"), new StableId("stop-handle"));
        NativeDeclaration cancel = Function("cancel", "native_cancel", new("void"), new StableId("cancel-handle"));
        NativeModule module = new("fixture-x64", "x86_64-fixture", 8,
            basic.Types.AddRange((ImmutableArray<NativeType>)[state, nativeHandle, registration, transform, notice, transformPointer, noticePointer]),
            basic.Declarations.AddRange((ImmutableArray<NativeDeclaration>)[new(new("registration-declaration"), "Class", "NativeRegistration", "NativeRegistration", registration.id, Location()) { role = "Struct" },
                register, stop, cancel, Parameter("transform-input", "transform", register.id, transformPointer.id),
                Parameter("notice-input", "notice", register.id, noticePointer.id), Parameter("data-input", "data", register.id, state.id),
                Parameter("stop-handle", "handle", stop.id, nativeHandle.id), Parameter("cancel-handle", "handle", cancel.id, nativeHandle.id)]), [])
        { language = SourceLanguage.C, byteOrder = NativeByteOrder.LittleEndian };
        InteropModule semantics = new CDeclarationAnalyzer().Analyze(module, "fixture", purpose: CDeclarationAnalysisPurpose.NativeBridgePlanning);
        Assert.DoesNotContain(semantics.diagnostics, diagnostic => diagnostic.isFailure);
        InteropFunction original = semantics.functions.Single(function => function.id == register.id);
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture.native-frame-stop-and-join");
        CallbackContract Callback(StableId callable) => new(callable, new(NativeLifetimeKind.Registration, null, evidence), evidence)
        {
            retained = true,
            userDataParameter = new("data-input"),
            callableUserDataIndex = 1,
            unregisterFunction = stop.id,
            unregisterQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations,
            threading = NativeCallbackThreading.AnyThread,
            registration = new(new("stop-handle"), NativeQuiescenceKind.NoActiveNativeInvocations, "any", evidence) { kind = NativeCallbackRegistrationKind.ReturnedHandle }
        };
        InteropFunction configured = original with
        {
            parameters = original.parameters.Select(parameter => parameter.id.value switch
        { "transform-input" => parameter with { callback = Callback(transform.id) }, "notice-input" => parameter with { callback = Callback(notice.id) }, _ => parameter }).ToImmutableArray(),
            asynchronous = asynchronous ? new(new("notice-input"), NativeQuiescenceKind.NoActiveNativeInvocations, evidence)
            { cancelFunction = cancel.id, cancellationHandleParameter = new("cancel-handle"), cancellationQuiescence = NativeQuiescenceKind.NoNewInvocations } : null
        };
        return Context(module) with { semantics = WithFunctions(semantics, semantics.functions.Replace(original, configured)) };
    }
    private static InteropModule WithFunctions(
        InteropModule module,
        ImmutableArray<InteropFunction> functions
    )
        => new(module.libraryId, module.targetId, module.pointerSize, module.types, functions, module.diagnostics) { byteOrder = module.byteOrder };
    private static LoweringContext Context(NativeModule module) => new(module,
        new(new(module.targetId), "fixture", "x64", "fixture", module.triple, new(), new(false, false), NativeByteOrder.LittleEndian)
        { sourceConventions = new(string.Empty, string.Empty, new("X86_64SysV", false)) }, "fixture", ["native.h"]);
    private static (ImmutableArray<NativeType> Types, ImmutableArray<NativeDeclaration> Declarations) Basics()
    {
        NativeType nothing = new(new("void"), NativeTypeCategory.Primitive, "void", 0, 1) { primitiveKind = "Void" };
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType sample = new(new("sample"), NativeTypeCategory.Record, "Sample", 4, 4) { declaration = new("sample-declaration") };
        NativeDeclaration record = new(new("sample-declaration"), "Class", "Sample", "Sample", sample.id, Location())
        { role = "Struct", isCompleteDefinition = true, children = [new("sample-value")] };
        NativeDeclaration field = new(new("sample-value"), "Field", "value", "Sample::value", integer.id, Location()) { parent = record.id, offset = 0 };
        return ([nothing, integer, sample], [record, field]);
    }
    private static SourceLocation Location() => new("native.h", 1, 1);
    private static NativeDeclaration Function(
        string id,
        string name,
        StableId result,
        params StableId[] parameters
    )
        => new(new(id), "Function", name, name, null, Location())
        { returnType = result, children = parameters.ToImmutableArray(), callingConvention = "C", linkage = "C", nativeLinkage = NativeLinkageKind.External };
    private static NativeDeclaration Parameter(
        string id,
        string name,
        StableId owner,
        StableId type
    )
        => new(new(id), "Parameter", name, name, type, Location()) { parent = owner };
    private sealed class ValueLowering : INativeTypeLowering
    {
        public bool CanLower(
            NativeTypeView type,
            LoweringContext context
        ) => type.id.value == "sample";
        public NativeTypeLoweringPlan Lower(
            NativeTypeView type,
            LoweringContext context,
            CancellationToken cancellationToken
        ) {
            BridgeType integer = new(new("custom-int"), BridgeTypeKind.Scalar, "int32_t");
            return new(integer.id, [integer], BridgeConversionKind.SourceExpression, BridgeConversionKind.SourceExpression)
            {
                parameterBindings = [new("left", integer.id), new("right", integer.id)],
                parameterExpression = LoweringRecipeCompiler.Expression("fixture.type", "({nativeType}){ {left} + {right} }"),
                resultExpression = LoweringRecipeCompiler.Expression("fixture.type", "{value}.value + 1")
            };
        }
    }
    private sealed class CallableLowering : INativeCallableLowering
    {
        public bool CanLower(
            NativeDeclaration callable,
            LoweringContext context
        ) => callable.id.value == "echo";
        public NativeCallableLoweringPlan Lower(
            NativeDeclaration callable,
            LoweringContext context,
            CancellationToken cancellationToken
        ) {
            BridgeType integer = new(new("custom-int"), BridgeTypeKind.Scalar, "int32_t");
            BridgeParameter parameter = new(new("custom-input"), "value", integer.id, new("sample"), BridgeConversionKind.SourceExpression)
            { conversionExpression = LoweringRecipeCompiler.Expression("fixture.callable", "({nativeType}){ {value} + 5 }") };
            BridgeExport export = new(new("custom-echo"), "custom_echo", [parameter], new(BridgeOperationKind.FreeCall, callable.id, null, null))
            {
                resultType = integer.id,
                nativeResultType = new("sample"),
                resultInOutputParameter = false,
                usesErrorChannel = false,
                resultConversion = BridgeConversionKind.SourceExpression,
                resultExpression = LoweringRecipeCompiler.Expression("fixture.callable", "{value}.value")
            };
            return new(export, [integer], []);
        }
    }
    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> files { get; } = [];
        public string WriteText(
            string relativePath,
            string content
        ) { files.Add(relativePath, content); return relativePath; }
    }
    private static async Task CompileAndRun(
        Sink sink,
        string header,
        string native,
        string consumer
    ) {
        string directory = Path.Combine(Path.GetTempPath(), "BGCS.NativeBridge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "include"));
            Directory.CreateDirectory(Path.Combine(directory, "src"));
            foreach (var file in sink.files) await File.WriteAllTextAsync(Path.Combine(directory, file.Key), file.Value);
            await File.WriteAllTextAsync(Path.Combine(directory, "native.h"), header);
            await File.WriteAllTextAsync(Path.Combine(directory, "native.c"), native);
            await File.WriteAllTextAsync(Path.Combine(directory, "consumer.c"), consumer);
            string compiler = Environment.GetEnvironmentVariable("BGCS_CC") ?? Environment.GetEnvironmentVariable("CC")
                ?? (OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin", "clang.exe") : "cc");
            string binary = Path.Combine(directory, OperatingSystem.IsWindows() ? "consumer.exe" : "consumer");
            await Run(compiler, ["-std=c17", "-Wall", "-Wextra", "-Werror", "-I", directory, "src/fixture.c", "native.c", "consumer.c", "-o", binary], directory);
            await Run(binary, [], directory);
        }
        finally
        {
            string expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BGCS.NativeBridge.Tests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(directory).StartsWith(expected, StringComparison.Ordinal)) throw new InvalidOperationException("A fixture cleanup path escaped its owned parent.");
            Directory.Delete(directory, true);
        }
        static async Task Run(
            string file,
            string[] arguments,
            string directory
        ) {
            ProcessStartInfo start = new(file) { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("The C fixture process did not start.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, file + " exited " + process.ExitCode + ":\n" + await output + await error);
        }
    }
    private const string ValueHeader = "#pragma once\ntypedef struct Sample { int value; } Sample;\nSample native_echo(Sample value);\n";
    private const string CallbackHeader = """
        #pragma once
        #include <stddef.h>
        typedef struct Sample { int value; } Sample;
        typedef Sample (*Transform)(Sample, void*);
        typedef void (*Notice)(Sample, void*);
        typedef struct NativeRegistration NativeRegistration;
        NativeRegistration* native_register(Transform, Notice, void*);
        void native_stop(NativeRegistration*);
        void native_cancel(NativeRegistration*);
        int native_invoke_all(void);
        void native_fail_next(void);
        """;
    private const string CallbackSource = """
        #include "native.h"
        #include <stdlib.h>
        struct NativeRegistration { Transform transform; Notice notice; void* data; struct NativeRegistration* next; };
        static NativeRegistration* head;
        static int fail_next;
        void native_fail_next(void) { fail_next = 1; }
        NativeRegistration* native_register(Transform transform, Notice notice, void* data) {
            if (fail_next) { fail_next = 0; return NULL; }
            NativeRegistration* result = malloc(sizeof(*result));
            if (!result) return NULL;
            *result = (NativeRegistration){ transform, notice, data, head }; head = result; return result;
        }
        void native_stop(NativeRegistration* value) {
            NativeRegistration** current = &head;
            while (*current != value) current = &(*current)->next;
            *current = value->next; free(value);
        }
        void native_cancel(NativeRegistration* value) { (void)value; }
        int native_invoke_all(void) {
            int total = 0;
            for (NativeRegistration* current = head; current; current = current->next) {
                Sample value = { 1 };
                if (current->transform) total += current->transform(value, current->data).value;
                if (current->notice) current->notice(value, current->data);
            }
            return total;
        }
        """;
}
