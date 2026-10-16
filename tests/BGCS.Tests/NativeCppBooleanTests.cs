using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Managed;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCppBooleanTests
{
    [Theory]
    [InlineData("package", false)]
    [InlineData("source", false)]
    [InlineData("package", true)]
    [InlineData("source", true)]
    public async Task NativeBool_NormalizesFriendlyValuesAndPreservesRawAndExplicitRecipe(string runtimeMode, bool custom)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-cpp-bool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "boolean.hpp"), """
                #include <stdexcept>
                namespace Demo {
                inline bool Negate(bool value) { return !value; }
                inline bool Checked(bool value) { if (!value) throw std::runtime_error("false input"); return value; }
                class Toggle {
                    bool m_value;
                public:
                    explicit Toggle(bool value) : m_value(value) {}
                    ~Toggle() noexcept = default;
                    bool Get() const { return m_value; }
                    bool Set(bool value) { bool previous = m_value; m_value = value; return previous; }
                    explicit operator bool() const { return m_value; }
                };
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "boolean", nativeName = "boolean_fixture" },
                source = new() { language = SourceLanguage.Cpp, headers = ["boolean.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.NativeBoolean", name = "BooleanApi" },
                cpp = new() { typeLowerings = custom ? [new TypeLoweringRecipe {
                    id = "explicit-bool-int", typePattern = "bool", cAbiType = "uint8_t",
                    parameterExpression = "static_cast<bool>({value})", resultExpression = "static_cast<uint8_t>({value})",
                    managedProjection = new ManagedRecipeProjection("int", "(byte)({value} != 0 ? 1 : 0)", "{value} != 0 ? 1 : 0")
                }] : ImmutableArray<TypeLoweringRecipe>.Empty },
                semantics = new() { ownedResults = [new() { function = "Demo::Toggle::Toggle(bool)", releaseThreadDomain = "any" }] },
                runtime = new() { mode = runtimeMode }, nativeBuild = new() { mode = "sources" }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            Invoke(Path.Combine(target, "managed"), Assert.Single(Directory.GetFiles(Path.Combine(target, "native", "bin"))), runtimeMode, custom);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Invoke(string sources, string binary, string runtimeMode, bool custom)
    {
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        string runtime = runtimeMode == "package" ? "BGCS.Runtime" : "Generated.NativeBoolean.Runtime";
        string consumer = """
            using Generated.NativeBoolean;
            public static unsafe class BooleanConsumer {
                public static int Run(string binary) {
                    using var api = BooleanApi.Load(binary);
                    if (api.Negate(FALSE_VALUE) != TRUE_VALUE || api.Negate(TRUE_VALUE) != FALSE_VALUE) return 1;
                    if (api.Checked(TRUE_VALUE) != TRUE_VALUE) return 2;
                    bool rejected = false;
                    try { api.Checked(FALSE_VALUE); }
                    catch (RUNTIME.NativeCallException) { rejected = true; }
                    if (!rejected) return 3;
                    using var toggle = api.CreateToggle(TRUE_VALUE);
                    if (toggle.Get() != TRUE_VALUE || toggle.ToBool() != TRUE_VALUE) return 4;
                    if (toggle.Set(FALSE_VALUE) != TRUE_VALUE || toggle.Get() != FALSE_VALUE || toggle.ToBool() != FALSE_VALUE) return 5;
                    byte raw = 255;
                    if (api.Raw.Negate(2, &raw, null) != 0 || raw != 0) return 6;
                    toggle.Dispose();
                    return toggle.releaseFailure is null ? 42 : 7;
                }
            }
            """.Replace("RUNTIME", "global::" + runtime, StringComparison.Ordinal)
                .Replace("TRUE_VALUE", custom ? "1" : "true", StringComparison.Ordinal).Replace("FALSE_VALUE", custom ? "0" : "false", StringComparison.Ordinal);
        CSharpCompilation compilation = CSharpCompilation.Create("NativeBoolean_" + Guid.NewGuid().ToString("N"),
            Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new(LanguageVersion.CSharp14), path))
                .Append(CSharpSyntaxTree.ParseText(consumer, new(LanguageVersion.CSharp14))),
            references.Concat(runtimeMode == "package" ? [typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location] : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("BGCS.NativeBoolean.Test", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            Assert.Equal(42, assembly.GetType("BooleanConsumer", true)!.GetMethod("Run")!.Invoke(null, [binary]));
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
