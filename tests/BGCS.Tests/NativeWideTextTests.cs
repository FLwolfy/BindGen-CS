using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeWideTextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CNativeCalls_PreserveUtf16Utf32AndTheActualWcharWidth(bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-wide-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "wide.h"), """
                #include <stdint.h>
                #include <wchar.h>
                uint32_t first16(const uint16_t* text);
                uint32_t next16(const uint16_t* text);
                uint32_t first32(const uint32_t* text);
                uint32_t next32(const uint32_t* text);
                uint32_t first_wchar(const wchar_t* text);
                uint32_t next_wchar(const wchar_t* text);
                uint32_t wchar_width(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "wide.c"), """
                #include "wide.h"
                #if defined(_WIN32)
                #define EXPORT __declspec(dllexport)
                #else
                #define EXPORT __attribute__((visibility("default")))
                #endif
                EXPORT uint32_t first16(const uint16_t* text) { return text[0]; }
                EXPORT uint32_t next16(const uint16_t* text) { return text[1]; }
                EXPORT uint32_t first32(const uint32_t* text) { return text[0]; }
                EXPORT uint32_t next32(const uint32_t* text) { return text[1]; }
                EXPORT uint32_t first_wchar(const wchar_t* text) { return (uint32_t)text[0]; }
                EXPORT uint32_t next_wchar(const wchar_t* text) { return (uint32_t)text[1]; }
                EXPORT uint32_t wchar_width(void) { return (uint32_t)sizeof(wchar_t); }
                """);
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            AnalysisResult facts = await host.AnalyzeAsync(AnalysisRequest.FromFiles(["wide.h"], baseDirectory: root) with { parseMacros = false });
            Assert.True(facts.success, facts.GetDiagnosticSummary());
            NativeDeclaration function = facts.module!.declarations.Single(declaration => declaration.name == "first_wchar" && declaration.kind == "Function");
            NativeType pointer = facts.module.GetType(facts.module.GetDeclaration(function.children[0]).type!.Value);
            while (pointer.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) pointer = facts.module.GetType(pointer.elementType!.Value);
            NativeType unit = facts.module.GetType(pointer.elementType!.Value);
            while (unit.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) unit = facts.module.GetType(unit.elementType!.Value);
            Assert.True(unit.size is 2 or 4);
            int width = checked((int)unit.size!.Value);
            ImmutableArray<StringParameterConfiguration>.Builder rules = ImmutableArray.CreateBuilder<StringParameterConfiguration>();
            foreach ((string export, int bytes) in new[] { ("first16", 2), ("next16", 2), ("first32", 4), ("next32", 4), ("first_wchar", width), ("next_wchar", width) })
                rules.Add(new()
                {
                    function = export,
                    parameter = "text",
                    encoding = bytes == 2 ? NativeTextEncoding.Utf16 : NativeTextEncoding.Utf32,
                    codeUnitWidth = bytes,
                    byteOrder = NativeTextByteOrder.Native,
                    lengthUnit = NativeLengthUnit.CodeUnits,
                    direction = NativeParameterDirection.Input,
                    lifetime = NativeLifetimeKind.Call,
                    retainsPointer = false,
                    zeroTerminated = true,
                    allowsNull = false
                });
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "wide", nativeName = "wide_fixture" },
                source = new() { headers = ["wide.h"], language = SourceLanguage.C, parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.NativeWide", name = "WideApi" },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                semantics = new() { stringParameters = rules.ToImmutable() },
                cache = new() { enabled = false }
            };
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target)));
            Invoke(Path.Combine(target, "managed"), binary, width);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Invoke(
        string sources,
        string binary,
        int width
    ) {
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("NativeWide_" + Guid.NewGuid().ToString("N"),
            Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new(LanguageVersion.CSharp14), path)),
            references.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("BGCS.NativeWide.Test", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            Type type = assembly.GetType("Generated.NativeWide.WideApi", true)!;
            using IDisposable api = (IDisposable)type.GetMethod("Load")!.InvokeFactory([binary])!;
            uint Call(
                string method,
                string text
            ) => (uint)type.GetMethod(method)!.Invoke(api, [text])!;
            Assert.Equal((uint)width, type.GetMethod("WcharWidth")!.Invoke(api, []));
            Assert.Equal((uint)65, Call("First16", "AB"));
            Assert.Equal((uint)65, Call("First32", "AB"));
            Assert.Equal((uint)65, Call("FirstWchar", "AB"));
            Assert.Equal(0xd83dU, Call("Next16", "A🐈"));
            Assert.Equal(0x1f408U, Call("Next32", "A🐈"));
            Assert.Equal(width == 2 ? 0xd83dU : 0x1f408U, Call("NextWchar", "A🐈"));
            Assert.IsType<EncoderFallbackException>(Assert.Throws<TargetInvocationException>(() => type.GetMethod("First32")!.Invoke(api, ["\ud800"])).InnerException);
            Assert.Equal((uint)65, Call("First32", "AB"));
        }
        finally { load.Unload(); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
