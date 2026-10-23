using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Bridge;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Plugins;
using BGCS.Core.Common;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using BGCS.Targets.Desktop.Targeting;
using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BGCS.Tests;

public sealed class NeutralCppBridgeTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Create(
        int value,
        out nint result,
        out nint error
    );
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Add(
        nint self,
        int delta,
        out int result,
        out nint error
    );
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Destroy(
        nint self,
        out nint error
    );
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Identity(
        ulong value,
        out ulong result,
        out nint error
    );
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Cast(
        nint self,
        out nint result,
        out nint error
    );
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ErrorMessage(nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ErrorRelease(nint error);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CppHost_ReparsesTypedBridgeAndPreservesOwnedResultsBeforeManagedCompilation(bool manifestPlugin)
    {
        string repository = FindRepository();
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        string worker = Path.Combine(repository, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-neutral-cpp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        nint library = 0;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temporary, "widget.hpp"), """
                #include <stdexcept>
                #include <cstdint>
                #include "dependency.hpp"
                namespace Sample {
                class Quantity { public: explicit Quantity(int value) : m_value(value) {} explicit operator int() const { return m_value; } private: int m_value; };
                inline Quantity add_quantity(Quantity left, Quantity right) { return Quantity(static_cast<int>(left) + static_cast<int>(right)); }
                enum class Flags : uint64_t { High = UINT64_C(9223372036854775808), All = UINT64_MAX };
                inline Flags echo_flags(Flags value) { return value; }
                struct First { double padding = 4; };
                struct Second { int tag = 7; };
                class Widget : public First, public Second {
                public:
                    explicit Widget(int value) : m_value(dependency_only(value)) {}
                    ~Widget() noexcept = default;
                    int Add(int delta) const { if (delta < 0) throw std::runtime_error("negative"); return m_value + delta; }
                    static uint64_t Identity(uint64_t value) { if (value == 1) throw std::runtime_error("identity failure"); return value; }
                    Widget& Self() { return *this; }
                    void AddTo(int& value) { value += m_value; }
                private: int m_value;
                };
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(temporary, "dependency.hpp"), "inline int dependency_only(int value) { return value; }");
            BindingConfiguration input = new()
            {
                schemaVersion = 2,
                library = new() { id = "widget", nativeName = "widget_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["widget.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.Widget", name = "WidgetApi", names = ImmutableDictionary<string, string>.Empty.Add("Sample::Widget::Identity", "Identity").Add("Sample::echo_flags", "EchoFlags").Add("Sample::add_quantity", "AddQuantity") },
                runtime = new() { mode = "source" },
                cache = new() { enabled = false }
            };
            System.Collections.Generic.List<BGCS.Tests.NeutralPlugin.QuantityPlugin> plugins = [];
            BindingHostBuilder builder = BindingGenerator.CreateBuilder().UseWorkerDirectory(worker);
            if (manifestPlugin)
            {
                string pluginRoot = Path.Combine(temporary, "plugins");
                Directory.CreateDirectory(pluginRoot);
                byte[] implementation = await File.ReadAllBytesAsync(typeof(BGCS.Tests.NeutralPlugin.QuantityPlugin).Assembly.Location);
                await File.WriteAllBytesAsync(Path.Combine(pluginRoot, "QuantityPlugin.dll"), implementation);
                PluginManifest manifest = new()
                {
                    schemaVersion = 1,
                    plugin = new BGCS.Tests.NeutralPlugin.QuantityPlugin().descriptor,
                    assembly = "QuantityPlugin.dll",
                    entryType = typeof(BGCS.Tests.NeutralPlugin.QuantityPlugin).FullName!,
                    assets = [new("QuantityPlugin.dll", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(implementation))))]
                };
                JsonSerializerOptions options = new() { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
                await File.WriteAllTextAsync(Path.Combine(pluginRoot, "plugin.json"), JsonSerializer.Serialize(manifest, options));
                input = input with { extensions = new() { plugins = ["plugins/plugin.json"] } };
            }
            else builder.AddPluginFactory(() => { var plugin = new BGCS.Tests.NeutralPlugin.QuantityPlugin(); plugins.Add(plugin); return plugin; });
            await using BindingHost host = builder.Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(input, temporary));
            Assert.True(result.success, result.GetDiagnosticSummary());
            if (!manifestPlugin)
            {
                Assert.Equal(2, plugins.Count);
                Assert.All(plugins, plugin => Assert.True(plugin.disposed));
                Assert.Null(plugins[0].lowering);
                Assert.True(plugins[1].lowering!.disposed);
                Assert.True(plugins[1].validator!.disposed);
                Assert.True(plugins[1].processor!.disposed);
            }
            string nativeRoot = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string methods = await File.ReadAllTextAsync(Path.Combine(nativeRoot, "managed", "Api", "WidgetApi.Methods.g.cs"));
            Assert.Contains("NativeCallException", methods);
            Assert.Contains("public ulong", methods);
            Assert.Contains("public ulong Identity(ulong value)", methods);
            Assert.True(File.Exists(Path.Combine(nativeRoot, "managed", "Types", "SampleWidget.g.cs")));
            Assert.True(File.Exists(Path.Combine(nativeRoot, "managed", "Types", "SampleFlags.g.cs")));
            string manifestPath = Path.Combine(nativeRoot, "native", "metadata", "bridge.module.json");
            NativeBridgePlan plan = JsonSerializer.Deserialize<NativeBridgePlan>(await File.ReadAllTextAsync(manifestPath))!;
            Assert.Contains(plan.exports, export => export.operation.kind == BridgeOperationKind.Construct && export.resultRelease is not null);
            Assert.Contains(plan.exports, export => export.operation.kind == BridgeOperationKind.BaseCast);
            Assert.DoesNotContain(plan.exports, export => export.operation.declaration is not null && plan.source.GetDeclaration(export.operation.declaration.Value).name == "dependency_only");
            using JsonDocument abi = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(nativeRoot, "metadata", "abi.manifest.json")));
            var owned = abi.RootElement.GetProperty("module").GetProperty("functions").EnumerateArray().SelectMany(function => function.GetProperty("parameters").EnumerateArray())
                .Where(parameter => parameter.GetProperty("ownership").ValueKind != JsonValueKind.Null).ToArray();
            Assert.Contains(owned, parameter => parameter.GetProperty("ownership").GetProperty("kind").GetInt32() == (int)NativeOwnershipKind.Owned);
            string output = Path.Combine(NativeConsumerHarness.NativeDirectory(nativeRoot), OperatingSystem.IsWindows() ? "widget_bridge.dll" : OperatingSystem.IsMacOS() ? "libwidget_bridge.dylib" : "libwidget_bridge.so");
            Assert.True(File.Exists(output));
            using JsonDocument verification = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(nativeRoot, "metadata", "verification.json")));
            Assert.False(verification.RootElement.TryGetProperty("nativeLinked", out _));
            Assert.False(verification.RootElement.TryGetProperty("nativeArtifactVerified", out _));
            Assert.False(verification.RootElement.TryGetProperty("invoked", out _));
            if (manifestPlugin) Assert.Equal(2, verification.RootElement.GetProperty("plugins")[0].GetProperty("inputs").GetArrayLength());
            InvokeGeneratedScalarWrapper(Path.Combine(nativeRoot, "managed"), output);
            library = NativeLibrary.Load(output);
            T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            BridgeExport constructor = Assert.Single(plan.exports, export => export.operation.kind == BridgeOperationKind.Construct
                && plan.source.GetType(export.operation.ownerType!.Value).spelling.EndsWith("Widget", StringComparison.Ordinal));
            BridgeExport addExport = Assert.Single(plan.exports.Where(export => export.operation.declaration is not null && plan.source.GetDeclaration(export.operation.declaration.Value).name == "Add"));
            Assert.Equal(0, Function<Create>(constructor.name)(40, out nint instance, out nint error));
            Assert.NotEqual(0, instance);
            Assert.Equal(0, error);
            try
            {
                Add add = Function<Add>(addExport.name);
                Assert.Equal(0, add(instance, 2, out int value, out error));
                Assert.Equal(42, value);
                Assert.Equal(0, error);
                Assert.Equal(1, add(instance, -1, out value, out error));
                Assert.Equal(0, value);
                Assert.NotEqual(0, error);
                Assert.Equal("negative", Marshal.PtrToStringUTF8(Function<ErrorMessage>(plan.errorMessageExport)(error)));
                Function<ErrorRelease>(plan.errorReleaseExport)(error);
                BridgeExport identity = Assert.Single(plan.exports.Where(export => export.operation.kind == BridgeOperationKind.StaticCall));
                Assert.Equal(0, Function<Identity>(identity.name)(ulong.MaxValue, out ulong high, out error));
                Assert.Equal(ulong.MaxValue, high);
                BridgeExport second = Assert.Single(plan.exports.Where(export => export.operation.kind == BridgeOperationKind.BaseCast && plan.source.GetType(export.operation.castTarget!.Value).spelling.EndsWith("Second", StringComparison.Ordinal)));
                Assert.Equal(0, Function<Cast>(second.name)(instance, out nint adjusted, out error));
                Assert.NotEqual(instance, adjusted);
                Assert.Equal(7, Marshal.ReadInt32(adjusted));
                Assert.Equal(2, add(0, 2, out value, out error));
                Function<ErrorRelease>(plan.errorReleaseExport)(error);
            }
            finally
            {
                BridgeExport destroy = plan.exports.Single(export => export.id == constructor.resultRelease);
                Assert.Equal(0, Function<Destroy>(destroy.name)(instance, out error));
                Assert.Equal(0, error);
            }
        }
        finally
        {
            if (library != 0) NativeLibrary.Free(library);
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static void InvokeGeneratedScalarWrapper(
        string managedRoot,
        string libraryPath
    ) {
        var trees = Directory.GetFiles(managedRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new CSharpParseOptions(LanguageVersion.CSharp14), path));
        string[] assemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!.Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("GeneratedWidget_" + Guid.NewGuid().ToString("N"), trees,
            assemblies.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        stream.Position = 0;
        AssemblyLoadContext context = new("BGCS.Cpp.Friendly.Test", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromStream(stream);
            Type type = assembly.GetType("Generated.Widget.WidgetApi", throwOnError: true)!;
            using IDisposable api = (IDisposable)type.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [libraryPath])!;
            MethodInfo identity = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance), method => method.ReturnType == typeof(ulong)
                && method.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == typeof(ulong));
            Assert.Equal(ulong.MaxValue, identity.Invoke(api, [ulong.MaxValue]));
            Assert.Equal(42, type.GetMethod("AddQuantity")!.Invoke(api, [20, 22]));
            Type flags = assembly.GetType("Generated.Widget.SampleFlags", true)!;
            Assert.Equal(typeof(ulong), Enum.GetUnderlyingType(flags));
            Assert.Equal(ulong.MaxValue, Convert.ToUInt64(Enum.Parse(flags, "All")));
            Assert.Equal(1UL << 63, Convert.ToUInt64(Enum.Parse(flags, "High")));
            object input = Enum.ToObject(flags, ulong.MaxValue);
            Assert.Equal(input, type.GetMethod("EchoFlags")!.Invoke(api, [input]));
            TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() => identity.Invoke(api, [(ulong)1]));
            Exception native = failure.InnerException!;
            Assert.Equal("Generated.Widget.Runtime.NativeCallException", native.GetType().FullName);
            Assert.Equal(1, native.GetType().GetProperty("status")!.GetValue(native));
            Assert.Contains("Identity", (string)native.GetType().GetProperty("exportName")!.GetValue(native)!);
        }
        finally { context.Unload(); }
    }

    private static string FindRepository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root is unavailable.");
    }
}
