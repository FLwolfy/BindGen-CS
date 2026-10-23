using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.NativeBridge.Recipes;
using Xunit;

namespace BGCS.Tests;

public sealed class CNativeBridgePipelineTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task AggregateValuesAndCallScopedCallbacksSurviveHeaderReparseAndRealConsumerInvocation(BindingImportMode mode)
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                #pragma once
                typedef struct Sample { int value; } Sample;
                typedef const Sample (*Transform)(Sample value, void* state);
                Sample Echo(Sample value);
                Sample Invoke(Transform callback, Sample value, void* state);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "native.c"), """
                #include "native.h"
                Sample Echo(Sample value) { value.value *= 2; return value; }
                Sample Invoke(Transform callback, Sample value, void* state) { return callback(value, state); }
                """);
            BindingConfiguration config = Configuration(mode) with { semantics = new() { callbackParameters = [new()
            { function = "Invoke", parameter = "callback", lifetime = NativeLifetimeKind.Call, retained = false,
                threading = NativeCallbackThreading.CallerThread, userDataParameter = "state", callableUserDataIndex = 1 }] } };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult generated = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(config, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            var target = Assert.Single(generated.targetModels);
            InteropFunction invoke = target.interop.functions.Single(function => function.sourceName == "Invoke");
            InteropParameter callback = invoke.parameters.Single(parameter => parameter.callback is not null);
            CallbackContract contract = callback.callback!;
            Assert.Equal(2, contract.requiredResultOutputIndex);
            Assert.Equal(1, contract.callableUserDataIndex);
            Assert.Contains(invoke.parameters, parameter => parameter.id == contract.userDataParameter);
            Assert.Equal(contract.callableType, ResolveCallable(target.interop, callback.type).id);
            Assert.Contains(generated.capabilities, capability => capability.declarationId == invoke.id
                && capability.reasons.Any(reason => reason.Contains("result-output", StringComparison.Ordinal)));
            string targetRoot = Path.Combine(generated.outputRoot, target.targetId);
            string factory = mode == BindingImportMode.FunctionTable ? "BridgeApi.Load(binary)" : "BridgeApi.Create()";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), Binary(targetRoot), config.library.nativeName, $$"""
                using Generated.CBridge;
                public static unsafe class CBridgeProbe {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        Sample input = new() { value = 11 }, result = default;
                        api.Raw.Echo(&input, &result);
                        if (result.value != 22) return 1;
                        int seed = 31;
                        api.Raw.Invoke(&Transform, &input, &seed, &result);
                        return result.value == 42 ? 42 : 2;
                    }
                    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
                    private static void Transform(Sample* input, void* state, Sample* result) { result->value = input->value + *(int*)state; }
                }
                """, "CBridgeProbe", root, sourceRuntime: false));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedAndAsyncRawCallbacksKeepNormalizedHandleAndUserDataThroughStopAndCancel(bool asynchronous)
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                #pragma once
                typedef struct Sample { int value; } Sample;
                typedef void (*Completion)(Sample value, int status, void* state);
                typedef struct Registration Registration;
                Registration* Register(Completion callback, void* state);
                void Stop(Registration* handle);
                void Cancel(Registration* handle);
                int FireAll(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "native.c"), """
                #include "native.h"
                #include <stdlib.h>
                struct Registration { Completion callback; void* state; struct Registration* next; };
                static Registration* head;
                Registration* Register(Completion callback, void* state) {
                    Registration* handle = malloc(sizeof(*handle)); if (!handle) return NULL;
                    *handle = (Registration){ callback, state, head }; head = handle; return handle;
                }
                void Stop(Registration* handle) {
                    Registration** current = &head; while (*current != handle) current = &(*current)->next;
                    *current = handle->next; free(handle);
                }
                void Cancel(Registration* handle) { Sample sample = { 3 }; handle->callback(sample, 1, handle->state); }
                int FireAll(void) {
                    int count = 0; Sample sample = { 5 };
                    for (Registration* current = head; current; current = current->next) { current->callback(sample, 0, current->state); count++; }
                    return count;
                }
                """);
            BindingConfiguration config = Configuration(BindingImportMode.FunctionTable) with { semantics = new()
            {
                callbackParameters = [new()
                { function = "Register", parameter = "callback", lifetime = asynchronous ? NativeLifetimeKind.Operation : NativeLifetimeKind.Registration,
                    retained = true, threading = NativeCallbackThreading.AnyThread, userDataParameter = "state", callableUserDataIndex = 2,
                    unregisterFunction = "Stop", unregisterQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations,
                    registration = new() { unregisterHandleParameter = "handle", nullResultQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations,
                        releaseThreadDomain = "any" } }],
                asyncOperations = asynchronous ? [new() { function = "Register", completionCallback = "callback",
                    completionQuiescence = NativeQuiescenceKind.NoNewInvocations, resultArgumentIndex = 0, statusArgumentIndex = 1,
                    successStatus = "0", cancellationStatus = "1", cancelFunction = "Cancel", cancelHandleParameter = "handle" }] : []
            } };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult generated = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(config, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            var target = Assert.Single(generated.targetModels);
            InteropFunction register = target.interop.functions.Single(function => function.sourceName == "Register");
            CallbackContract callback = register.parameters.Single(parameter => parameter.callback is not null).callback!;
            InteropFunction stop = target.interop.functions.Single(function => function.id == callback.unregisterFunction);
            Assert.Equal(register.returnType, stop.parameters.Single().type);
            Assert.Equal(stop.parameters.Single().id, callback.registration!.unregisterHandleParameter);
            Assert.Equal(2, callback.callableUserDataIndex);
            Assert.Contains(register.parameters, parameter => parameter.id == callback.userDataParameter);
            if (asynchronous)
            {
                Assert.NotNull(register.asynchronous);
                InteropFunction cancel = target.interop.functions.Single(function => function.id == register.asynchronous!.cancelFunction);
                Assert.Equal(register.returnType, cancel.parameters.Single().type);
                Assert.Equal(cancel.parameters.Single().id, register.asynchronous!.cancellationHandleParameter);
                Assert.Contains(generated.capabilities, capability => capability.declarationId == register.id
                    && capability.reasons.Any(reason => reason.Contains("scalar", StringComparison.OrdinalIgnoreCase)));
            }
            string targetRoot = Path.Combine(generated.outputRoot, target.targetId);
            string cancelCall = asynchronous ? "api.Raw.Cancel(first); if (a != 9) return 3;" : string.Empty;
            int expected = asynchronous ? 14 : 11;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), Binary(targetRoot), config.library.nativeName, $$"""
                using Generated.CBridge;
                public static unsafe class CBridgeProbe {
                    public static int Run(string binary) {
                        using var api = BridgeApi.Load(binary);
                        int a = 1, b = 2;
                        var first = api.Raw.Register(&Complete, &a);
                        var second = api.Raw.Register(&Complete, &b);
                        if (first == null || second == null) return 1;
                        if (api.Raw.FireAll() != 2 || a != 6 || b != 7) return 2;
                        {{cancelCall}}
                        if (api.Raw.FireAll() != 2 || a != {{expected}} || b != 12) return 4;
                        api.Raw.Stop(first);
                        if (api.Raw.FireAll() != 1 || b != 17) return 5;
                        api.Raw.Stop(second);
                        return api.Raw.FireAll() == 0 ? 42 : 6;
                    }
                    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
                    private static void Complete(Sample* sample, int status, void* state) { *(int*)state += sample->value; }
                }
                """, "CBridgeProbe", root, sourceRuntime: false));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicCoreLoweringPortsProduceCustomCConversionsThroughTheEntirePipeline(bool callable)
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "typedef struct Sample { int value; } Sample;\nSample Echo(Sample value);\n");
            await File.WriteAllTextAsync(Path.Combine(root, "native.c"), "#include \"native.h\"\nSample Echo(Sample value) { value.value *= 2; return value; }\n");
            BindingHostBuilder builder = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot());
            if (callable) builder.RegisterFactory<INativeCallableLowering>(new("fixture.custom-c-call", ExtensionPort.NativeCallableLowering), static _ => new CustomCallable());
            else builder.RegisterFactory<INativeTypeLowering>(new("fixture.custom-c-type", ExtensionPort.NativeTypeLowering), static _ => new CustomType());
            BindingConfiguration config = Configuration(BindingImportMode.FunctionTable);
            await using BindingHost host = builder.Build();
            GenerationResult generated = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(config, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            var target = Assert.Single(generated.targetModels);
            Assert.Equal(callable ? 1 : 2, Assert.Single(target.interop.functions).parameters.Length);
            Assert.Equal(Assert.Single(target.interop.functions).parameters.Length, Assert.Single(target.interop.functions).parameters.Select(parameter => parameter.sourceName ?? parameter.name).Distinct(StringComparer.Ordinal).Count());
            string targetRoot = Path.Combine(generated.outputRoot, target.targetId);
            string arguments = callable ? "7" : "10, 20";
            int expected = callable ? 24 : 61;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), Binary(targetRoot), config.library.nativeName, $$"""
                using Generated.CBridge;
                public static unsafe class CBridgeProbe {
                    public static int Run(string binary) {
                        using var api = BridgeApi.Load(binary);
                        return api.Raw.Echo({{arguments}}) == {{expected}} ? 42 : 0;
                    }
                }
                """, "CBridgeProbe", root, sourceRuntime: false));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task FiniteCVariantsRebindEachBufferLengthToItsOwnPhysicalPrefix()
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "#pragma once\nint Sum(const int* values, int count, ...);\n");
            await File.WriteAllTextAsync(Path.Combine(root, "native.c"), """
                #include "native.h"
                #include <stdarg.h>
                int Sum(const int* values, int count, ...) {
                    int result = 0; for (int index = 0; index < count; index++) result += values[index];
                    va_list arguments; va_start(arguments, count); result += va_arg(arguments, int); va_end(arguments);
                    return result;
                }
                """);
            BindingConfiguration config = Configuration(BindingImportMode.FunctionTable);
            config = config with
            {
                source = config.source with { variadicVariants = [
                    new() { function = "Sum", name = "SumShort", parameters = [new("extra", "short")] },
                    new() { function = "Sum", name = "SumInt", parameters = [new("extra", "int")] }] },
                semantics = new() { bufferParameters = [new() { function = "Sum", parameter = "values", lengthParameter = "count",
                    lengthUnit = NativeLengthUnit.Elements, direction = NativeParameterDirection.Input, lifetime = NativeLifetimeKind.Call,
                    retainsPointer = false, allowsNull = false }] }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult generated = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(config, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(generated.targetModels);
            Assert.Equal(2, target.interop.functions.Length);
            Assert.Equal(2, target.interop.functions.SelectMany(function => function.parameters.Where(parameter => parameter.buffer is not null))
                .Select(parameter => parameter.buffer!.lengthParameter).Distinct().Count());
            foreach (InteropFunction function in target.interop.functions)
            {
                InteropParameter buffer = Assert.Single(function.parameters, parameter => parameter.buffer is not null);
                InteropParameter length = Assert.Single(function.parameters, parameter => parameter.id == buffer.buffer!.lengthParameter);
                Assert.Equal("count", length.sourceName);
                Assert.Equal(function.parameters[0].id, buffer.id);
                Assert.Equal("Int", target.interop.GetType(buffer.buffer!.elementType).primitiveKind);
            }
            string targetRoot = Path.Combine(generated.outputRoot, target.targetId);
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), Binary(targetRoot), config.library.nativeName, """
                using Generated.CBridge;
                public static class CBridgeProbe {
                    public static int Run(string binary) {
                        using var api = BridgeApi.Load(binary);
                        int[] values = [10, 20];
                        return api.SumShort(values, (short)12) == 42 && api.SumInt(values, 12) == 42 ? 42 : 1;
                    }
                }
                """, "CBridgeProbe", root, sourceRuntime: false));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task AnonymousTypedefStorageAndRealCTagsKeepDistinctSourceNamespacesAcrossReparse()
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                #pragma once
                typedef enum { Choice_First = 7 } Choice;
                typedef struct { Choice selected; } AnonymousRecord;
                struct RealTag { int value; };
                const AnonymousRecord Echo(const AnonymousRecord value);
                int ReadFirst(const Choice* value);
                int ReadSecond(const Choice* value);
                int ReadTag(const struct RealTag* value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "native.c"), """
                #include "native.h"
                const AnonymousRecord Echo(const AnonymousRecord value) { return value; }
                int ReadFirst(const Choice* value) { return (int)*value; }
                int ReadSecond(const Choice* value) { return (int)*value + 1; }
                int ReadTag(const struct RealTag* value) { return value->value; }
                """);
            BindingConfiguration config = Configuration(BindingImportMode.FunctionTable);
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult generated = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(config, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(generated.targetModels);
            string targetRoot = Path.Combine(generated.outputRoot, target.targetId);
            string header = await File.ReadAllTextAsync(Directory.GetFiles(Path.Combine(targetRoot, "native", "include"), "*.h").Single());
            Assert.DoesNotContain("enum Choice", header, StringComparison.Ordinal);
            Assert.DoesNotContain("struct AnonymousRecord", header, StringComparison.Ordinal);
            Assert.DoesNotContain("const const", header, StringComparison.Ordinal);
            Assert.Contains("struct RealTag", header, StringComparison.Ordinal);
            NativeDeclaration originalEcho = target.native.declarations.Single(declaration => declaration.kind == "Function" && declaration.name == "Echo");
            Assert.Contains("Const", target.native.GetType(originalEcho.returnType!.Value).qualifier!, StringComparison.Ordinal);
            InteropFunction physicalEcho = target.interop.functions.Single(function => function.sourceName == "Echo");
            InteropType outputPointer = target.interop.GetType(physicalEcho.parameters[^1].type);
            Assert.Null(target.interop.GetType(outputPointer.elementType!.Value).qualifier);
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), Binary(targetRoot), config.library.nativeName, """
                using Generated.CBridge;
                public static unsafe class CBridgeProbe {
                    public static int Run(string binary) {
                        using var api = BridgeApi.Load(binary);
                        AnonymousRecord input = new() { selected = (Choice)7 }, output = default;
                        api.Raw.Echo(&input, &output);
                        Choice choice = output.selected; RealTag tagged = new() { value = 27 };
                        return api.Raw.ReadFirst(&choice) + api.Raw.ReadSecond(&choice) + api.Raw.ReadTag(&tagged) == 42 ? 42 : 1;
                    }
                }
                """, "CBridgeProbe", root, sourceRuntime: false));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task UnicodeCTypeSpellingsSurviveBridgeReparseAndNativeConsumption()
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                #pragma once
                typedef enum { Choice_First = 7 } 選択;
                typedef struct { 選択 selected; } 値;
                struct 𐐀Tag { int value; };
                const 値 Echo(const 値 value);
                int ReadFirst(const 選択* value);
                int ReadSecond(const 選択* value);
                int ReadTag(const struct 𐐀Tag* value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "native.c"), """
                #include "native.h"
                const 値 Echo(const 値 value) { return value; }
                int ReadFirst(const 選択* value) { return (int)*value; }
                int ReadSecond(const 選択* value) { return (int)*value + 1; }
                int ReadTag(const struct 𐐀Tag* value) { return value->value; }
                """);
            BindingConfiguration config = Configuration(BindingImportMode.FunctionTable);
            config = config with { api = config.api with { names = ImmutableDictionary<string, string>.Empty
                .Add("選択", "Choice").Add("値", "AnonymousRecord").Add("𐐀Tag", "SupplementaryTag") } };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult generated = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(config, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(generated.targetModels);
            string targetRoot = Path.Combine(generated.outputRoot, target.targetId);
            string header = await File.ReadAllTextAsync(Directory.GetFiles(Path.Combine(targetRoot, "native", "include"), "*.h").Single());
            Assert.DoesNotContain("enum 選択", header, StringComparison.Ordinal);
            Assert.DoesNotContain("struct 値", header, StringComparison.Ordinal);
            Assert.DoesNotContain("const const", header, StringComparison.Ordinal);
            Assert.Contains("struct 𐐀Tag", header, StringComparison.Ordinal);
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), Binary(targetRoot), config.library.nativeName, """
                using Generated.CBridge;
                public static unsafe class CBridgeProbe {
                    public static int Run(string binary) {
                        using var api = BridgeApi.Load(binary);
                        AnonymousRecord input = new() { selected = (Choice)7 }, output = default;
                        api.Raw.Echo(&input, &output);
                        Choice choice = output.selected; SupplementaryTag tagged = new() { value = 27 };
                        return api.Raw.ReadFirst(&choice) + api.Raw.ReadSecond(&choice) + api.Raw.ReadTag(&tagged) == 42 ? 42 : 1;
                    }
                }
                """, "CBridgeProbe", root, sourceRuntime: false));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class CustomType : INativeTypeLowering
    {
        public bool CanLower(NativeTypeView type, LoweringContext context) => type.type.category == NativeTypeCategory.Record && type.declaration?.name == "Sample";
        public NativeTypeLoweringPlan Lower(NativeTypeView type, LoweringContext context, CancellationToken cancellationToken)
        {
            BridgeType integer = new(new("custom-c-integer"), BridgeTypeKind.Scalar, "int32_t");
            return new(integer.id, [integer], BridgeConversionKind.SourceExpression, BridgeConversionKind.SourceExpression)
            { parameterBindings = [new("left", integer.id), new("right", integer.id)],
                parameterExpression = LoweringRecipeCompiler.Expression("fixture.custom-c-type", "({nativeType}){ {left} + {right} }"),
                resultExpression = LoweringRecipeCompiler.Expression("fixture.custom-c-type", "{value}.value + 1") };
        }
    }
    private sealed class CustomCallable : INativeCallableLowering
    {
        public bool CanLower(NativeDeclaration callable, LoweringContext context) => callable.name == "Echo";
        public NativeCallableLoweringPlan Lower(NativeDeclaration callable, LoweringContext context, CancellationToken cancellationToken)
        {
            NativeDeclaration original = context.module.GetDeclaration(callable.children.Single());
            BridgeType integer = new(new("custom-c-integer"), BridgeTypeKind.Scalar, "int32_t");
            BridgeParameter input = new(new("custom-c-input"), "value", integer.id, original.type!.Value, BridgeConversionKind.SourceExpression)
            { conversionExpression = LoweringRecipeCompiler.Expression("fixture.custom-c-call", "({nativeType}){ {value} + 5 }") };
            return new(new(new("custom-c-export"), "custom_c_echo", [input], new(BridgeOperationKind.FreeCall, callable.id, null, null))
            { resultType = integer.id, nativeResultType = callable.returnType, resultInOutputParameter = false, usesErrorChannel = false,
                resultConversion = BridgeConversionKind.SourceExpression, resultExpression = LoweringRecipeCompiler.Expression("fixture.custom-c-call", "{value}.value") }, [integer], []);
        }
    }
    private static BindingConfiguration Configuration(BindingImportMode mode) => new()
    {
        schemaVersion = 2, library = new() { id = "c_pipeline", nativeName = "c_pipeline_native" },
        source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
        api = new() { @namespace = "Generated.CBridge", name = "BridgeApi", importMode = mode }, cache = new() { enabled = false }
    };
    private static InteropType ResolveCallable(InteropModule module, StableId id)
    {
        InteropType type = module.GetType(id);
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified or NativeTypeCategory.Pointer) type = module.GetType(type.elementType!.Value);
        return type;
    }
    private static string Binary(string targetRoot) => Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)).Single(path => Path.GetExtension(path) is ".dll" or ".so" or ".dylib");
    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "BGCS.CNativeBridge.Pipeline." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return root;
    }
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
