using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCallScopedCallbackTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task NativeCallsRootDelegateAndStaticThunksAndObserveFallbackBeforeManagedRethrow(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-call-scoped-callback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "callbacks.h"), """
                #pragma once
                #include <stdbool.h>
                #if defined(_WIN32)
                #define CALLBACK_EXPORT __declspec(dllexport)
                #else
                #define CALLBACK_EXPORT __attribute__((visibility("default")))
                #endif
                typedef int (*PlainHandler)(int value);
                typedef int (*StateHandler)(int value, void* state);
                typedef bool (*BoolHandler)(bool value, void* state);
                typedef void (*VoidHandler)(int value, void* state);
                CALLBACK_EXPORT int CallPlain(PlainHandler callback, int value);
                CALLBACK_EXPORT int CallState(StateHandler callback, int value, void* state);
                CALLBACK_EXPORT int CallBool(BoolHandler callback, int value, void* state);
                CALLBACK_EXPORT int CallVoid(VoidHandler callback, int value, void* state);
                CALLBACK_EXPORT int CallPointer(StateHandler callback, int value, const int* pointer, void* state);
                CALLBACK_EXPORT int CallComposed(StateHandler callback, const char* text, int* values, int capacity, int* written, int* state, void* userData);
                CALLBACK_EXPORT bool CallBooleanResult(StateHandler callback, int value, void* state);
                CALLBACK_EXPORT int Last(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "callbacks.c"), """
                #include "callbacks.h"
                #include <string.h>
                static int last_value;
                CALLBACK_EXPORT int CallPlain(PlainHandler callback, int value) { int result = callback(value); last_value = result; return result; }
                CALLBACK_EXPORT int CallState(StateHandler callback, int value, void* state) { int result = callback(value, state); last_value = result; return result; }
                CALLBACK_EXPORT int CallBool(BoolHandler callback, int value, void* state) { int result = callback(value != 0, state); last_value = result; return result; }
                CALLBACK_EXPORT int CallVoid(VoidHandler callback, int value, void* state) { callback(value, state); last_value = value; return value; }
                CALLBACK_EXPORT int CallPointer(StateHandler callback, int value, const int* pointer, void* state) { return callback(value + *pointer, state); }
                CALLBACK_EXPORT int CallComposed(StateHandler callback, const char* text, int* values, int capacity, int* written, int* state, void* userData) {
                    for (int index = 0; index < capacity; ++index) values[index] += callback((unsigned char)text[0] + index, userData);
                    *written = capacity; *state += callback(0, userData); return (int)strlen(text) + *state;
                }
                CALLBACK_EXPORT bool CallBooleanResult(StateHandler callback, int value, void* state) { return callback(value, state) != 0; }
                CALLBACK_EXPORT int Last(void) { return last_value; }
                """);
            CallbackParameterConfiguration Rule(string function, bool state, string? fallback) => new()
            {
                function = function, parameter = "callback", lifetime = NativeLifetimeKind.Call, retained = false,
                threading = NativeCallbackThreading.CallerThread, failureReturn = fallback,
                userDataParameter = state ? "state" : null, callableUserDataIndex = state ? 1 : null
            };
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "callbacks", nativeName = "callback_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["callbacks.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                semantics = new()
                {
                    callbackParameters = [Rule("CallPlain", false, "-7"), Rule("CallState", true, "-9"), Rule("CallBool", true, "0"), Rule("CallVoid", true, null),
                        Rule("CallPointer", true, "-1"), Rule("CallComposed", true, "-9") with { userDataParameter = "userData" }, Rule("CallBooleanResult", true, "-1")],
                    stringParameters = [new() { function = "CallComposed", parameter = "text", encoding = NativeTextEncoding.Utf8, codeUnitWidth = 1,
                        direction = NativeParameterDirection.Input, lengthUnit = NativeLengthUnit.CodeUnits, lifetime = NativeLifetimeKind.Call,
                        zeroTerminated = true, retainsPointer = false, allowsNull = false }],
                    bufferParameters = [new() { function = "CallComposed", parameter = "values", capacityParameter = "capacity", writtenCountParameter = "written",
                        lengthUnit = NativeLengthUnit.Elements, direction = NativeParameterDirection.InputOutput, lifetime = NativeLifetimeKind.Call,
                        retainsPointer = false, allowsNull = false }],
                    valueParameters = [new() { function = "CallComposed", parameter = "state", direction = NativeParameterDirection.InputOutput,
                        lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false }]
                },
                api = new() { @namespace = "Generated.Callbacks", name = "CallbackApi", importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                 cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            Assert.Equal(7, target.interop.functions.SelectMany(function => function.parameters).Count(parameter => parameter.callback is not null));
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            string context = sourceRuntime ? "CallbackApiContext" : "CallbackApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string staticCall = sourceRuntime ? (mode == BindingImportMode.FunctionTable ? "CallbackApi.Load(binary);" : "CallbackApi.Create();") + " if (CallbackApi.CallState(value => value + 12, 30) != 42) return 10; CallbackApi.Dispose();" : "";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, "callback_fixture", $$"""
                using System;
                using Generated.Callbacks;
                using ApiContext = Generated.Callbacks.{{context}};
                public static unsafe class CallbackConsumer {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        using var observer = {{factory}};
                        int offset = 10;
                        if (api.CallPlain(value => { GC.Collect(); GC.WaitForPendingFinalizers(); return value + offset; }, 32) != 42) return 1;
                        if (api.CallState(value => { GC.Collect(); return value + 11; }, 31) != 42) return 2;
                        try { api.CallPlain(_ => throw new InvalidOperationException("plain failure"), 1); return 3; }
                        catch (InvalidOperationException error) { if (error.Message != "plain failure" || observer.Last() != -7) return 4; }
                        try { api.CallState(_ => throw new InvalidOperationException("state failure"), 1); return 5; }
                        catch (InvalidOperationException error) { if (error.Message != "state failure" || observer.Last() != -9) return 6; }
                        if (api.CallBool(value => value == 0 ? (byte)1 : (byte)0, 0) != 1 || api.CallBool(value => value == 0 ? (byte)1 : (byte)0, 1) != 0) return 7;
                        int notified = 0;
                        if (api.CallVoid(value => notified = value, 42) != 42 || notified != 42) return 8;
                        try { api.CallVoid(_ => throw new InvalidOperationException("void failure"), 123); return 9; }
                        catch (InvalidOperationException error) { if (error.Message != "void failure" || observer.Last() != 123) return 11; }
                        int pointed = 12;
                        if (api.CallPointer(value => value + 10, 20, &pointed) != 42) return 14;
                        int[] values = [1, 2]; int state = 10;
                        if (api.CallComposed(value => { GC.Collect(); return value + 3; }, "AB", values, out int written, ref state) != 15
                            || state != 13 || written != 2 || values[0] != 69 || values[1] != 71) return 15;
                        values = [1, 2]; state = 10; written = -1;
                        try { api.CallComposed(_ => throw new InvalidOperationException("composed failure"), "AB", values, out written, ref state); return 16; }
                        catch (InvalidOperationException error) {
                            if (error.Message != "composed failure" || state != 1 || written != 2 || values[0] != -8 || values[1] != -7) return 17;
                        }
                        if (!api.CallBooleanResult(value => value, 1) || api.CallBooleanResult(value => value, 0)) return 18;
                        {{staticCall}}
                        if (api.CallState(value => { api.Dispose(); return value + 10; }, 32) != 42) return 12;
                        try { api.CallPlain(value => value, 1); return 13; } catch (ObjectDisposedException) { }
                        return 42;
                    }
                }
                """, "CallbackConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
