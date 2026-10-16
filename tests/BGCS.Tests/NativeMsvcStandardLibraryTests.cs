using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Analysis.Bridge;
using BGCS.Configuration;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeMsvcStandardLibraryTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Construct(out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Getter(nint self, out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntegerValue(nint self, out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(nint self, out ulong result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Destroy(nint self, out nint error);

    [Fact]
    [Trait("Category", "WindowsNativeProvider")]
    public async Task MsvcBuildsAndInvokesUserClassWithStringVectorAndOptionalStorage()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs MSVC STL effective facts 目录 & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint library = 0, instance = 0;
        BridgeExport? destructor = null;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "box.hpp"), Source);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "msvc_stl", nativeName = "msvc_stl_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["box.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                runtime = new() { mode = "source" }, cache = new() { enabled = false },
                nativeBuild = new() { mode = "sources", provider = "msvc" }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            string module = Assert.Single(Directory.GetFiles(built.outputRoot, "bridge.module.json", SearchOption.AllDirectories));
            CppBridgeModule bridge = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(module))!;
            BridgeExport create = Assert.Single(bridge.exports, export => export.operation.kind == BridgeOperationKind.Construct
                && bridge.source.GetType(export.operation.ownerType!.Value).spelling == "Fixture::Box");
            destructor = Assert.Single(bridge.exports, export => export.id == create.resultRelease);
            string binary = Assert.Single(Directory.GetFiles(built.outputRoot, "msvc_stl_bridge.dll", SearchOption.AllDirectories));
            library = NativeLibrary.Load(binary);
            T Function<T>(BridgeExport export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export.name));
            BridgeExport Method(string name) => Assert.Single(bridge.exports, export => export.operation.declaration is { } declaration
                && bridge.source.GetDeclaration(declaration).qualifiedName == "Fixture::Box::" + name);
            Assert.Equal(0, Function<Construct>(create)(out instance, out nint error));
            Assert.NotEqual(0, instance);
            Assert.Equal(0, error);
            Assert.Equal(0, Function<IntegerValue>(Method("Read"))(instance, out int total, out error));
            Assert.Equal(42, total);
            Assert.Equal(0, error);
            foreach (string name in new[] { "Text", "Values", "Optional" })
            {
                BridgeExport getter = Method(name);
                Assert.Equal(0, Function<Getter>(getter)(instance, out nint holder, out error));
                Assert.NotEqual(0, holder);
                Assert.Equal(0, error);
                try
                {
                    var type = StandardLibraryFactReader.Resolve(bridge.source, getter.nativeResultType!.Value).id;
                    BridgeExport access = Assert.Single(bridge.exports, export => export.operation.ownerType == type
                        && export.operation.standardValue?.action == (name == "Optional" ? StandardValueAction.GetValue : StandardValueAction.Count));
                    if (name == "Optional")
                    {
                        Assert.Equal(0, Function<IntegerValue>(access)(holder, out int value, out error));
                        Assert.Equal(6, value);
                    }
                    else
                    {
                        Assert.Equal(0, Function<Count>(access)(holder, out ulong count, out error));
                        Assert.Equal(3UL, count);
                    }
                    Assert.Equal(0, error);
                }
                finally { Assert.Equal(0, Function<Destroy>(Assert.Single(bridge.exports, export => export.id == getter.resultRelease))(holder, out error)); }
            }
        }
        finally
        {
            if (instance != 0 && library != 0 && destructor is not null)
                Assert.Equal(0, Marshal.GetDelegateForFunctionPointer<Destroy>(NativeLibrary.GetExport(library, destructor.name))(instance, out _));
            if (library != 0) NativeLibrary.Free(library);
            Directory.Delete(root, recursive: true);
        }
    }

    internal const string Source = """
        #pragma once
        #include <string>
        #include <vector>
        #include <optional>
        namespace Fixture {
        class Box {
            std::string m_text;
            std::vector<int> m_values;
            std::optional<int> m_optional;
        public:
            Box() : m_text("ABC"), m_values{10, 11, 12}, m_optional(6) {}
            ~Box() noexcept = default;
            int Read() const { int total = static_cast<int>(m_text.size()) + m_optional.value(); for (int value : m_values) total += value; return total; }
            std::string Text() const { return m_text; }
            std::vector<int> Values() const { return m_values; }
            std::optional<int> Optional() const { return m_optional; }
        };
        }
        """;

    [Theory]
    [InlineData("assignable-layout")]
    [InlineData("assignable-sfinae")]
    [InlineData("member-interconvertible")]
    [InlineData("corresponding-member")]
    [Trait("Category", "WindowsNativeProvider")]
    public async Task ConcreteUnsupportedNativeIntrinsicNeverBecomesAnAssumedFact(string scenario)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs MSVC concrete intrinsic " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = scenario switch
            {
                "assignable-layout" => """
                    #ifdef __clang__
                    constexpr bool selected = true;
                    #else
                    constexpr bool selected = __is_assignable_no_precondition_check(int&, int);
                    #endif
                    struct Probe { int value[selected ? 1 : 2]; };
                    """,
                "assignable-sfinae" => """
                    #ifdef __clang__
                    #define CHECK(T, U) __is_assignable(T, U)
                    #else
                    #define CHECK(T, U) __is_assignable_no_precondition_check(T, U)
                    #endif
                    template<class T, std::enable_if_t<CHECK(T&, T), int> = 0> int choose(T);
                    long choose(...);
                    struct Probe { decltype(choose(1)) value; };
                    """,
                "member-interconvertible" => """
                    struct Probe { int value; };
                    #ifdef __clang__
                    constexpr bool selected = true;
                    #else
                    constexpr bool selected = __is_pointer_interconvertible_with_class(Probe, &Probe::value);
                    #endif
                    static_assert(selected);
                    """,
                _ => """
                    struct Probe { int value; };
                    struct Other { int value; };
                    #ifdef __clang__
                    constexpr bool selected = true;
                    #else
                    constexpr bool selected = __is_corresponding_member(Probe, Other, &Probe::value, &Other::value);
                    #endif
                    static_assert(selected);
                    """
            };
            await File.WriteAllTextAsync(Path.Combine(root, "probe.hpp"), "#include <type_traits>\n" + source);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "msvc_intrinsic", nativeName = "msvc_intrinsic_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["probe.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false,
                    selection = new() { includeDeclarations = ["Probe"] } },
                runtime = new() { mode = "source" }, cache = new() { enabled = false },
                nativeBuild = new() { mode = "sources", provider = "msvc" }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, root)));
            Assert.False(built.success);
            Assert.Contains("BGCS effective source requires an unsupported native", built.GetDiagnosticSummary());
            Assert.False(Directory.Exists(Path.Combine(root, "Generated")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
