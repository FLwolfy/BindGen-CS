using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCountedStringInputTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task CountedUnicodePreservesActualNativeUnitsAndRejectsNarrowCountsBeforeInvocation(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-counted-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "text.h"), """
                #include <stdint.h>
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                API int counted_utf8(const uint8_t* text, uint8_t count);
                API int counted_signed(const int8_t* text, uint8_t count);
                API int counted_utf16le(const uint16_t* text, uint32_t count);
                API int counted_utf16be(const uint16_t* text, uint32_t count);
                API int counted_utf32be(const uint32_t* text, uint32_t count);
                API int counted_calls(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "text.c"), """
                #include "text.h"
                #include <string.h>
                static int calls;
                API int counted_calls(void) { return calls; }
                API int counted_utf8(const uint8_t* text, uint8_t count) {
                    ++calls; if(!text)return count==0?10:-10; if(count==0)return 11;
                    static const unsigned char expected[]={65,0,240,159,152,128,66};
                    return count==7 && memcmp(text,expected,7)==0 ? 42 : -42;
                }
                static uint32_t unit(const void* text, int index, int width, int big) {
                    const unsigned char* bytes=(const unsigned char*)text+index*width; uint32_t value=0;
                    for(int n=0;n<width;n++) value=(value<<8)|bytes[big?n:width-1-n]; return value;
                }
                API int counted_signed(const int8_t* text, uint8_t count) { return counted_utf8((const uint8_t*)text,count); }
                static int utf16(const uint16_t* text, uint32_t count, int big) {
                    ++calls; if(!text)return count==0?10:-10; if(count==0)return 11;
                    return count==5 && unit(text,0,2,big)==65 && unit(text,1,2,big)==0 && unit(text,2,2,big)==0xd83d
                        && unit(text,3,2,big)==0xde00 && unit(text,4,2,big)==66 ? 42 : -42;
                }
                API int counted_utf16le(const uint16_t* text, uint32_t count) { return utf16(text,count,0); }
                API int counted_utf16be(const uint16_t* text, uint32_t count) { return utf16(text,count,1); }
                API int counted_utf32be(const uint32_t* text, uint32_t count) {
                    ++calls; if(!text)return count==0?10:-10; if(count==0)return 11;
                    return count==16 && unit(text,0,4,1)==65 && unit(text,1,4,1)==0 && unit(text,2,4,1)==0x1f600
                        && unit(text,3,4,1)==66 ? 42 : -42;
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "counted-text", nativeName = "counted_text_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["text.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.CountedNative", name = "TextApi", importMode = mode,
                    names = ImmutableDictionary<string, string>.Empty.Add("counted_utf8", "Utf8").Add("counted_signed", "Signed").Add("counted_utf16le", "Utf16Le")
                        .Add("counted_utf16be", "Utf16Be").Add("counted_utf32be", "Utf32Be").Add("counted_calls", "Calls") },
                semantics = new() { stringParameters = [Text("counted_utf8", NativeTextEncoding.Utf8, 1, NativeTextByteOrder.Native, NativeLengthUnit.CodeUnits),
                    Text("counted_signed", NativeTextEncoding.Utf8, 1, NativeTextByteOrder.Native, NativeLengthUnit.CodeUnits),
                    Text("counted_utf16le", NativeTextEncoding.Utf16, 2, NativeTextByteOrder.LittleEndian, NativeLengthUnit.CodeUnits),
                    Text("counted_utf16be", NativeTextEncoding.Utf16, 2, NativeTextByteOrder.BigEndian, NativeLengthUnit.CodeUnits),
                    Text("counted_utf32be", NativeTextEncoding.Utf32, 4, NativeTextByteOrder.BigEndian, NativeLengthUnit.Bytes)] },
                nativeBuild = new() { mode = "sources", sources = ["text.c"] }, runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            Assert.DoesNotContain(built.generation.diagnostics, diagnostic => diagnostic.code == "BGCS-FRIENDLY-OMITTED");
            string targetRoot = Path.Combine(built.outputRoot, Assert.Single(built.generation.targetModels).targetId);
            string factory = mode == BindingImportMode.FunctionTable ? "TextApi.Load(binary)" : "TextApi.Create()";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"),
                Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin"))), configuration.library.nativeName, $$"""
                using System;
                using System.Text;
                using Generated.CountedNative;
                public static unsafe class CountedConsumer {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        const string value="A\0😀B";
                        if(api.Utf8(value)!=42 || api.Signed(value)!=42 || api.Utf16Le(value)!=42 || api.Utf16Be(value)!=42 || api.Utf32Be(value)!=42)return 1;
                        if(api.Utf8(null)!=10 || api.Utf8("")!=11 || api.Utf16Le(null)!=10 || api.Utf32Be("")!=11)return 2;
                        int calls=api.Raw.Calls();
                        try{api.Utf8(new string('A',256));return 3;}catch(OverflowException){}
                        try{api.Utf8(new string('A',301));return 4;}catch(ArgumentOutOfRangeException){}
                        try{api.Utf16Be("\ud800");return 5;}catch(EncoderFallbackException){}
                        if(api.Raw.Calls()!=calls)return 6;
                        byte* raw=stackalloc byte[]{65,0,240,159,152,128,66};
                        if(api.Raw.Utf8(raw,7)!=42 || api.Raw.Calls()!=calls+1)return 7;
                        return 42;
                    }
                }
                """, "CountedConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static StringParameterConfiguration Text(string function, NativeTextEncoding encoding, int width, NativeTextByteOrder order, NativeLengthUnit unit)
        => new() { function = function, parameter = "text", encoding = encoding, codeUnitWidth = width, byteOrder = order, lengthUnit = unit,
            direction = NativeParameterDirection.Input, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = true, zeroTerminated = false,
            lengthParameter = "count", lengthDirection = NativeParameterDirection.Input, maximumCodeUnits = 300 };

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
