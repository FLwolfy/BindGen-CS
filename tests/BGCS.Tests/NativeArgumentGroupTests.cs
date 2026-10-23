using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Bridge;
using BGCS.Core.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeArgumentGroupTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Sum(nint data, ulong length, int bias, out int result, out nint error);

    [Fact]
    public async Task MultiCarrierRecipe_ReparsesExactAbiBuildsAndInvokesOriginalNativeArgument()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-argument-group-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint library = 0, data = 0;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "fixture.hpp"), """
                #include <cstddef>
                namespace Demo {
                class Range {
                public:
                    Range(const int* data, std::size_t length) : m_data(data), m_length(length) {}
                    int Sum() const { int value = 0; for (std::size_t i = 0; i < m_length; ++i) value += m_data[i]; return value; }
                private: const int* m_data; std::size_t m_length;
                };
                inline int Sum(Range range, int bias) { return range.Sum() + bias; }
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "arguments", nativeName = "arguments_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["fixture.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                bridge = new() { typeLowerings = [new()
                {
                    id = "range", typePattern = "Demo::Range", cAbiType = "uint64_t",
                    abiParameters = [new("data", "const int32_t*"), new("length", "uint64_t")],
                    parameterExpression = "{nativeType}({data}, static_cast<std::size_t>({length}))"
                }] },
                api = new() { @namespace = "Generated.Arguments", name = "ArgumentsApi" },
                runtime = new() { mode = "source" }, cache = new() { enabled = false }};
            string worker = Path.Combine(Repository(), "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", BuildConfiguration(), "net10.0");
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(worker).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root), compileManaged: true);
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(Path.Combine(root, "Generated")));
            NativeBridgePlan plan = JsonSerializer.Deserialize<NativeBridgePlan>(await File.ReadAllTextAsync(Path.Combine(target, "native", "metadata", "bridge.module.json")))!;
            BridgeExport sum = plan.exports.Single(export => export.operation.declaration is { } id && plan.source.GetDeclaration(id).qualifiedName == "Demo::Sum");
            Assert.Equal(3, sum.parameters.Length);
            Assert.Equal(2, sum.operation.argumentGroups.Length);
            using JsonDocument abi = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(target, "metadata", "abi.manifest.json")));
            JsonElement contract = abi.RootElement.GetProperty("module").GetProperty("functions").EnumerateArray()
                .Single(function => function.GetProperty("sourceQualifiedName").ValueKind == JsonValueKind.String && function.GetProperty("sourceQualifiedName").GetString() == "Demo::Sum");
            Assert.Equal(3, contract.GetProperty("error").GetProperty("evidence").GetProperty("level").GetInt32());
            string binary = Path.Combine(NativeConsumerHarness.NativeDirectory(target), OperatingSystem.IsWindows() ? "arguments_bridge.dll" : OperatingSystem.IsMacOS() ? "libarguments_bridge.dylib" : "libarguments_bridge.so");
            library = NativeLibrary.Load(binary);
            data = Marshal.AllocHGlobal(3 * sizeof(int));
            Marshal.Copy(new[] { 10, 11, 12 }, 0, data, 3);
            Sum invoke = Marshal.GetDelegateForFunctionPointer<Sum>(NativeLibrary.GetExport(library, sum.name));
            Assert.Equal(0, invoke(data, 3, 9, out int value, out nint error));
            Assert.Equal(42, value);
            Assert.Equal(0, error);
        }
        finally
        {
            if (data != 0) Marshal.FreeHGlobal(data);
            if (library != 0) NativeLibrary.Free(library);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string BuildConfiguration() => AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
    private static string Repository()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "BindGen-CS.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("The repository root is unavailable.");
    }
}
