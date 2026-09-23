using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using BGCS.Intermediate;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class AdvancedCppLoweringTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ArraySum(nint values, nuint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint MakePair(int first, int second);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadHolder(nint holder);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint RoundPath([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long AddNanoseconds(long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyHolder(nint holder);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint PointerCast(nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateObject();

    [Fact]
    public void Generate_AdvancedStandardAdapters_ShouldCompileAndInvokeNativeBridge()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-advanced-adapters-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "advanced.hpp");
        string output = Path.Combine(temp, "out");
        string library = Path.Combine(temp,
            OperatingSystem.IsWindows() ? "advanced.dll" : OperatingSystem.IsMacOS() ? "libadvanced.dylib" : "libadvanced.so");
        File.WriteAllText(header,
            """
            #include <array>
            #include <chrono>
            #include <expected>
            #include <filesystem>
            #include <map>
            #include <set>
            #include <variant>
            namespace Demo {
            inline int ArraySum(std::array<int, 3> values) { return values[0] + values[1] + values[2]; }
            inline std::map<int, int> MakeMap(int first, int second) { return {{1, first}, {2, second}}; }
            inline int ReadMap(std::map<int, int> values) { return values[1] + values[2]; }
            inline std::set<int> MakeSet(int first, int second) { return {first, second}; }
            inline int ReadSet(std::set<int> values) { int result = 0; for (int value : values) result += value; return result; }
            inline std::variant<int, double> MakeVariant(int value, int reserved) { (void)reserved; return value; }
            inline int ReadVariant(std::variant<int, double> value) { return std::get<int>(value); }
            inline std::expected<int, int> MakeExpected(int value, int reserved) { (void)reserved; return value >= 0 ? std::expected<int, int>(value) : std::unexpected(-value); }
            inline int ReadExpected(std::expected<int, int> value) { return value.has_value() ? *value : -value.error(); }
            inline std::filesystem::path RoundPath(std::filesystem::path value) { return value; }
            inline std::chrono::nanoseconds AddDuration(std::chrono::nanoseconds value) { return value + std::chrono::nanoseconds(5); }
            using Point = std::chrono::time_point<std::chrono::system_clock, std::chrono::nanoseconds>;
            inline Point AddTime(Point value) { return value + std::chrono::nanoseconds(7); }
            }
            """);

        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());
            generator.Generate(header, output);

            Assert.True(generator.LastResult?.Success,
                string.Join(Environment.NewLine, generator.LastResult?.Diagnostics.Select(diagnostic => diagnostic.Message) ?? []));
            string bridgeHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            string bridgeSource = File.ReadAllText(Path.Combine(output, "src", "Classes.cpp"));
            Assert.Contains("Demo_ArraySum(int* values, size_t values_count)", bridgeHeader);
            Assert.Contains("Demo_MakeMap", bridgeHeader);
            Assert.Contains("Demo_MakeSet", bridgeHeader);
            Assert.Contains("Demo_MakeVariant", bridgeHeader);
            Assert.Contains("Demo_MakeExpected", bridgeHeader);
            Assert.Contains("Demo_RoundPath(const char* value)", bridgeHeader);
            Assert.Contains("Demo_AddDuration(int64_t value)", bridgeHeader);
            Assert.Contains("TryGetAt", bridgeHeader);
            Assert.Contains("TryGetValue", bridgeHeader);
            Assert.Contains("std::filesystem::path(std::u8string", bridgeSource);
            Assert.Contains("std::chrono::duration_cast", bridgeSource);
            Assert.Contains(generator.LastResult!.Module!.Functions,
                function => function.NativeName == "MakeMap" &&
                    function.ReturnMarshalling.Ownership == BindingOwnership.Owned &&
                    function.ReturnMarshalling.RequiresCleanup);
            Assert.True(CompileGeneratedBridgeDll(output, temp, library, out string diagnostics), diagnostics);

            nint native = NativeLibrary.Load(library);
            try
            {
                ArraySum arraySum = Load<ArraySum>(native, "Demo_ArraySum");
                int[] values = [4, 5, 6];
                GCHandle pinnedValues = GCHandle.Alloc(values, GCHandleType.Pinned);
                try
                {
                    Assert.Equal(15, arraySum(pinnedValues.AddrOfPinnedObject(), 3));
                }
                finally
                {
                    pinnedValues.Free();
                }

                VerifyOwnedHolder(native, bridgeHeader, "Demo_MakeMap", "Demo_ReadMap", 11, 12, 23);
                VerifyOwnedHolder(native, bridgeHeader, "Demo_MakeSet", "Demo_ReadSet", 7, 9, 16);
                VerifyOwnedHolder(native, bridgeHeader, "Demo_MakeVariant", "Demo_ReadVariant", 41, 0, 41);
                VerifyOwnedHolder(native, bridgeHeader, "Demo_MakeExpected", "Demo_ReadExpected", 37, 0, 37);

                RoundPath roundPath = Load<RoundPath>(native, "Demo_RoundPath");
                Assert.Equal("folder/file.txt", Marshal.PtrToStringUTF8(roundPath("folder/file.txt")));
                Assert.Equal(105, Load<AddNanoseconds>(native, "Demo_AddDuration")(100));
                Assert.Equal(107, Load<AddNanoseconds>(native, "Demo_AddTime")(100));
            }
            finally
            {
                NativeLibrary.Free(native);
            }
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_ComplexInheritance_ShouldPerformRealPointerAdjustmentAndCheckedDowncast()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-inheritance-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "inheritance.hpp");
        string output = Path.Combine(temp, "out");
        string library = Path.Combine(temp,
            OperatingSystem.IsWindows() ? "inheritance.dll" : OperatingSystem.IsMacOS() ? "libinheritance.dylib" : "libinheritance.so");
        File.WriteAllText(header,
            "class BaseA { public: int a = 1; virtual ~BaseA() = default; }; " +
            "class BaseB { public: double b = 2; virtual ~BaseB() = default; }; " +
            "class Derived : public BaseA, public BaseB { public: int c = 3; }; ");
        try
        {
            Cpp2CCodeGenerator generator = new(new Cpp2CGeneratorConfig());
            generator.Generate(header, output);
            Assert.True(generator.LastResult?.Success,
                string.Join(Environment.NewLine, generator.LastResult?.Diagnostics.Select(diagnostic => diagnostic.Message) ?? []));
            Assert.True(CompileGeneratedBridgeDll(output, temp, library, out string diagnostics), diagnostics);

            nint native = NativeLibrary.Load(library);
            try
            {
                nint derived = Load<CreateObject>(native, "DerivedCreate")();
                Assert.NotEqual(0, derived);
                nint baseB = Load<PointerCast>(native, "DerivedAsBaseB")(derived);
                Assert.NotEqual(0, baseB);
                Assert.NotEqual(derived, baseB);
                Assert.Equal(derived, Load<PointerCast>(native, "DerivedFromBaseB")(baseB));
                Load<DestroyHolder>(native, "DerivedDestroy")(derived);
            }
            finally
            {
                NativeLibrary.Free(native);
            }
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_ExplicitFullAndPartialTemplateSpecializations_ShouldSelectOnlyRequestedInstances()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-specializations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "specializations.hpp");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "namespace Demo { " +
            "template<class T> class Selector { public: int Kind() const { return 0; } }; " +
            "template<class T> class Selector<T*> { public: int Kind() const { return 1; } }; " +
            "template<> class Selector<int> { public: int Kind() const { return 2; } }; " +
            "}");
        Cpp2CGeneratorConfig config = new();
        config.TemplateInstantiations.Add("Demo::Selector<int>");
        config.TemplateInstantiations.Add("Demo::Selector<float*>");
        try
        {
            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);
            Assert.True(generator.LastResult?.Success,
                string.Join(Environment.NewLine, generator.LastResult?.Diagnostics.Select(diagnostic => diagnostic.Message) ?? []));
            string bridgeHeader = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            Assert.Contains("Selector", bridgeHeader);
            Assert.Contains("Kind", bridgeHeader);
            Assert.DoesNotContain(generator.LastResult!.Diagnostics,
                diagnostic => diagnostic.Code == BindingDiagnosticCodes.CppInstantiation);
            Assert.True(CompileGeneratedBridgeDll(output, temp,
                Path.Combine(temp, OperatingSystem.IsMacOS() ? "libspecializations.dylib" : OperatingSystem.IsWindows() ? "specializations.dll" : "libspecializations.so"),
                out string diagnostics), diagnostics);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    private static void VerifyOwnedHolder(nint native, string header, string createName, string readName,
        int first, int second, int expected)
    {
        MakePair create = Load<MakePair>(native, createName);
        ReadHolder read = Load<ReadHolder>(native, readName);
        nint holder = create(first, second);
        Assert.NotEqual(0, holder);
        Match declaration = Regex.Match(header,
            $@"API\((?<holder>[A-Za-z_][A-Za-z0-9_]*)\*\)\s+{Regex.Escape(createName)}\(");
        Assert.True(declaration.Success, $"Unable to resolve holder declaration for {createName}.");
        DestroyHolder destroy = Load<DestroyHolder>(native, declaration.Groups["holder"].Value + "Destroy");
        try
        {
            Assert.Equal(expected, read(holder));
        }
        finally
        {
            destroy(holder);
        }
    }

    private static T Load<T>(nint library, string symbol) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, symbol));

    private static bool CompileGeneratedBridgeDll(string output, string sourceDirectory, string library,
        out string diagnostics)
    {
        string? compiler = CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp);
        if (compiler == null)
        {
            diagnostics = "C++ compiler not available.";
            return false;
        }
        ProcessStartInfo start = new(compiler)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        string[] arguments = OperatingSystem.IsWindows()
            ? [
                "-std=c++23", "-shared", "-I", Path.Combine(output, "include"),
                "-I", sourceDirectory, Path.Combine(output, "src", "Classes.cpp"), "-o", library
            ]
            : [
                "-std=c++23", OperatingSystem.IsMacOS() ? "-dynamiclib" : "-shared", "-fPIC",
                "-I", Path.Combine(output, "include"), "-iquote", sourceDirectory,
                Path.Combine(output, "src", "Classes.cpp"), "-o", library
            ];
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        diagnostics = stdout.Result + stderr.Result;
        return process.ExitCode == 0;
    }
}
