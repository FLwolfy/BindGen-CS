using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Inputs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCppOverloadTests
{
    [Fact]
    public async Task NativeOverloads_ConstructorsMembersAndOperators_UseNaturalNamesAndExactSignatures()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-cpp-overloads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "overloads.hpp"), """
                #include <stdexcept>
                namespace Demo {
                inline int Evaluate(int value) { return value + 2; }
                inline double Evaluate(double value) { return value + 2.5; }
                class Counter {
                    inline static int s_live = 0;
                    int m_value;
                public:
                    explicit Counter(int value) : m_value(value) { if (value < 0) throw std::runtime_error("negative"); ++s_live; }
                    explicit Counter(double value) : m_value(static_cast<int>(value * 2)) { ++s_live; }
                    ~Counter() noexcept { --s_live; }
                    int Evaluate(int delta) const { return m_value + delta; }
                    double Evaluate(double delta) const { return m_value + delta + 0.25; }
                    int operator+(int delta) const { return m_value + delta; }
                    explicit operator int() const { return m_value; }
                    static int Live() { return s_live; }
                };
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "overloads", nativeName = "overloads_fixture" },
                source = new() { language = SourceLanguage.Cpp, headers = ["overloads.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.NativeOverloads", name = "OverloadApi" },
                semantics = new() { ownedResults = [new() { function = "Demo::Counter::Counter(int)", releaseThreadDomain = "any" },
                    new() { function = "Demo::Counter::Counter(double)", releaseThreadDomain = "any" }] },
                runtime = new() { mode = "package" },  cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target)));
            Invoke(Path.Combine(target, "managed"), binary);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Invoke(string sources, string binary)
    {
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("NativeOverloads_" + Guid.NewGuid().ToString("N"),
            Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new(LanguageVersion.CSharp14), path)),
            references.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("BGCS.NativeOverloads.Test", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            Type apiType = assembly.GetType("Generated.NativeOverloads.OverloadApi", true)!;
            using IDisposable api = (IDisposable)apiType.GetMethod("Load")!.InvokeFactory([binary])!;
            Assert.Equal(42, apiType.GetMethod("Evaluate", [typeof(int)])!.Invoke(api, [40]));
            Assert.Equal(42.5, apiType.GetMethod("Evaluate", [typeof(double)])!.Invoke(api, [40.0]));
            Assert.Equal(0, apiType.GetMethod("Live")!.Invoke(api, null));
            Assert.Throws<TargetInvocationException>(() => apiType.GetMethod("CreateCounter", [typeof(int)])!.Invoke(api, [-1]));
            Assert.Equal(0, apiType.GetMethod("Live")!.Invoke(api, null));
            using IDisposable integer = (IDisposable)apiType.GetMethod("CreateCounter", [typeof(int)])!.Invoke(api, [40])!;
            using IDisposable real = (IDisposable)apiType.GetMethod("CreateCounter", [typeof(double)])!.Invoke(api, [2.5])!;
            Type objectType = integer.GetType();
            Assert.Equal(objectType, real.GetType());
            Assert.Equal(42, objectType.GetMethod("Evaluate", [typeof(int)])!.Invoke(integer, [2]));
            Assert.Equal(42.25, objectType.GetMethod("Evaluate", [typeof(double)])!.Invoke(integer, [2.0]));
            Assert.Equal(7, objectType.GetMethod("Add")!.Invoke(real, [2]));
            Assert.Equal(40, objectType.GetMethod("ToInt")!.Invoke(integer, null));
            Assert.Equal(2, apiType.GetMethod("Live")!.Invoke(api, null));
            integer.Dispose();
            real.Dispose();
            Assert.Null(objectType.GetProperty("releaseFailure")!.GetValue(integer));
            Assert.Null(objectType.GetProperty("releaseFailure")!.GetValue(real));
            Assert.Equal(0, apiType.GetMethod("Live")!.Invoke(api, null));
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
