using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCppWideTextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CppStatusAndOwnedMethods_PreserveCompilerCodeUnitsAndCheckedTextLifetimes(bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-cpp-wide-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "wide.hpp"), """
                #include <cstdint>
                #include <stdexcept>
                namespace Wide {
                inline uint32_t First16(const char16_t* text) { return text[0]; }
                inline uint32_t Next16(const char16_t* text) { return text[1]; }
                inline uint32_t First32(const char32_t* text) { return text[0]; }
                inline uint32_t Next32(const char32_t* text) { return text[1]; }
                inline uint32_t FirstWide(const wchar_t* text) { return static_cast<uint32_t>(text[0]); }
                inline uint32_t NextWide(const wchar_t* text) { return static_cast<uint32_t>(text[1]); }
                inline char16_t Echo16(char16_t value) { return value; }
                inline char32_t Echo32(char32_t value) { return value; }
                inline wchar_t EchoWide(wchar_t value) { return value; }
                inline uint32_t WcharWidth() { return sizeof(wchar_t); }
                inline uint32_t Nullable32(const char32_t* text) { return text ? text[0] : 0; }
                inline uint32_t Check32(const char32_t* text) { if (text[0] == U'!') throw std::runtime_error("invalid text"); return text[0]; }
                inline uint32_t RawOnly(const char32_t* text) { return text[0]; }
                inline uint32_t Retained32(const char32_t* text) { return text[0]; }
                inline uint32_t Unknown32(const char32_t* text) { return text[0]; }
                class Probe {
                    inline static int s_live = 0;
                    uint32_t m_value;
                public:
                    explicit Probe(const wchar_t* text) : m_value(static_cast<uint32_t>(text[0])) {
                        if (m_value == '!') throw std::runtime_error("invalid constructor"); ++s_live;
                    }
                    ~Probe() noexcept { --s_live; }
                    void Set(const wchar_t* text) {
                        if (text[0] == L'!') throw std::runtime_error("invalid member"); m_value = static_cast<uint32_t>(text[0]);
                    }
                    uint32_t Read() const { return m_value; }
                    static int Live() { return s_live; }
                };
                }
                """);
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            AnalysisResult analysis = await host.AnalyzeAsync(new(["wide.hpp"], SourceLanguage.Cpp, root) { parseMacros = false });
            Assert.True(analysis.success, analysis.GetDiagnosticSummary());
            NativeModule module = analysis.module!;
            NativeType Unit(string function)
            {
                NativeDeclaration callable = module.declarations.Single(declaration => declaration.qualifiedName == "Wide::" + function && declaration.kind == "Function");
                NativeType pointer = module.GetType(module.GetDeclaration(callable.children[0]).type!.Value);
                while (pointer.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) pointer = module.GetType(pointer.elementType!.Value);
                NativeType unit = module.GetType(pointer.elementType!.Value);
                while (unit.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) unit = module.GetType(unit.elementType!.Value);
                return unit;
            }
            Assert.Equal(2, Unit("First16").size);
            Assert.False(Unit("First16").isSigned);
            Assert.Equal(4, Unit("First32").size);
            Assert.False(Unit("First32").isSigned);
            NativeType wchar = Unit("FirstWide");
            Assert.True(wchar.size is 2 or 4);
            Assert.NotNull(wchar.isSigned);
            int width = checked((int)wchar.size!.Value);
            ImmutableArray<StringParameterConfiguration>.Builder rules = ImmutableArray.CreateBuilder<StringParameterConfiguration>();
            foreach ((string callable, int bytes) in new[]
                {
                    ("First16", 2), ("Next16", 2), ("First32", 4), ("Next32", 4), ("FirstWide", width), ("NextWide", width),
                    ("Check32", 4), ("Nullable32", 4), ("Probe::Probe", width), ("Probe::Set", width)
                })
                rules.Add(Text("Wide::" + callable, bytes) with { allowsNull = callable == "Nullable32" });
            rules.Add(Text("Wide::Retained32", 4) with { retainsPointer = true });
            rules.Add(Text("Wide::Unknown32", 4) with { encoding = NativeTextEncoding.Unknown });
            ImmutableDictionary<string, string>.Builder names = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (string name in new[] { "First16", "Next16", "First32", "Next32", "FirstWide", "NextWide", "Echo16", "Echo32", "EchoWide", "WcharWidth", "Check32", "Nullable32", "RawOnly", "Retained32", "Unknown32" })
                names.Add("Wide::" + name, name);
            names.Add("Wide::Probe::Probe", "CreateProbe");
            names.Add("Wide::Probe::Live", "Live");
            names.Add("Wide::Probe::Set", "Set");
            names.Add("Wide::Probe::Read", "Read");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "cpp_wide", nativeName = "cpp_wide_fixture" },
                source = new() { headers = ["wide.hpp"], language = SourceLanguage.Cpp, parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.CppWide", name = "WideApi", names = names.ToImmutable() },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                semantics = new() { stringParameters = rules.ToImmutable(), ownedResults = [new() { function = "Wide::Probe::Probe", releaseThreadDomain = "any" }] },
                nativeBuild = new() { mode = "sources" }, cache = new() { enabled = false }
            };
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(target, "native", "bin")));
            Invoke(Path.Combine(target, "managed"), binary, width, wchar.isSigned!.Value);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static StringParameterConfiguration Text(string callable, int bytes) => new()
    {
        function = callable, parameter = "text", encoding = bytes == 2 ? NativeTextEncoding.Utf16 : NativeTextEncoding.Utf32,
        codeUnitWidth = bytes, byteOrder = NativeTextByteOrder.Native, lengthUnit = NativeLengthUnit.CodeUnits,
        direction = NativeParameterDirection.Input, lifetime = NativeLifetimeKind.Call, retainsPointer = false, zeroTerminated = true, allowsNull = false
    };

    private static void Invoke(string sources, string binary, int width, bool wcharSigned)
    {
        string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("NativeCppWide_" + Guid.NewGuid().ToString("N"),
            Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new(LanguageVersion.CSharp14), path)),
            paths.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("BGCS.NativeCppWide.Test", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            Type type = assembly.GetType("Generated.CppWide.WideApi", true)!;
            using IDisposable api = (IDisposable)type.GetMethod("Load")!.InvokeFactory([binary])!;
            object? Call(string method, params object?[] values) => type.GetMethod(method)!.Invoke(api, values);
            Assert.Equal((uint)width, Call("WcharWidth"));
            Assert.Equal((uint)65, Call("First16", "AB"));
            Assert.Equal((uint)65, Call("First32", "AB"));
            Assert.Equal((uint)65, Call("FirstWide", "AB"));
            Assert.Equal(0xd83dU, Call("Next16", "A🐈"));
            Assert.Equal(0x1f408U, Call("Next32", "A🐈"));
            Assert.Equal(width == 2 ? 0xd83dU : 0x1f408U, Call("NextWide", "A🐈"));
            Assert.Equal(0U, Call("Nullable32", [null]));
            Assert.Equal((ushort)0xfffe, Call("Echo16", (ushort)0xfffe));
            Assert.Equal(0x1f408U, Call("Echo32", 0x1f408U));
            Type carrier = width == 2 ? wcharSigned ? typeof(short) : typeof(ushort) : wcharSigned ? typeof(int) : typeof(uint);
            Assert.Equal(carrier, type.GetMethod("EchoWide")!.ReturnType);
            object value = Convert.ChangeType(65, carrier);
            Assert.Equal(value, Call("EchoWide", value));
            Assert.IsType<EncoderFallbackException>(Assert.Throws<TargetInvocationException>(() => Call("First32", "\ud800")).InnerException);
            Assert.Throws<TargetInvocationException>(() => Call("Check32", "!"));
            Assert.Equal(65U, Call("Check32", "AB"));
            Assert.Null(type.GetMethod("RawOnly"));
            Assert.Null(type.GetMethod("Retained32"));
            Assert.Null(type.GetMethod("Unknown32"));
            Type raw = type.GetProperty("Raw")!.PropertyType;
            foreach (string name in new[] { "RawOnly", "Retained32", "Unknown32" }) Assert.True(raw.GetMethod(name)!.GetParameters()[0].ParameterType.IsPointer);
            Assert.IsType<EncoderFallbackException>(Assert.Throws<TargetInvocationException>(() => Call("CreateProbe", "\ud800")).InnerException);
            Assert.Throws<TargetInvocationException>(() => Call("CreateProbe", "!"));
            Assert.Equal(0, Call("Live"));
            using IDisposable probe = (IDisposable)Call("CreateProbe", "AB")!;
            Type objectType = probe.GetType();
            Assert.Equal(65U, objectType.GetMethod("Read")!.Invoke(probe, null));
            objectType.GetMethod("Set")!.Invoke(probe, ["B"]);
            Assert.Equal(66U, objectType.GetMethod("Read")!.Invoke(probe, null));
            Assert.Throws<TargetInvocationException>(() => objectType.GetMethod("Set")!.Invoke(probe, ["!"]));
            Assert.Equal(66U, objectType.GetMethod("Read")!.Invoke(probe, null));
            Assert.Equal(1, Call("Live"));
            api.Dispose();
            probe.Dispose();
            Assert.Null(objectType.GetProperty("releaseFailure")!.GetValue(probe));
            ValueTask closing = (ValueTask)type.GetMethod("DisposeAsync")!.Invoke(api, null)!;
            Assert.True(closing.IsCompletedSuccessfully, "Text encoding or failed construction retained a module resource lease.");
        }
        finally { load.Unload(); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
