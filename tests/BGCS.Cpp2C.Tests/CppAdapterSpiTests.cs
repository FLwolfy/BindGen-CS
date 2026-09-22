using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BGCS.Core.Extensibility;
using BGCS.Cpp2C.Adapters;
using BGCS.Cpp2C.Build;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class CppAdapterSpiTests
{
    [Fact]
    public void RegisteredAdapters_ChangeTypeAndCallableLoweringWithoutGeneratorChanges()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-adapter-spi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "GeneratedBridge");
        File.WriteAllText(header, "int add(int left, int right) { return left + right; }\n");
        try
        {
            Cpp2CGeneratorConfig config = new();
            config.Adapters.Register(new Int32TypeAdapter());
            config.Adapters.Register(new RenameAddAdapter());

            Assert.Contains(config.Adapters.TypeAdapters, adapter => adapter.Name == "builtin.utf8-string");
            Assert.Equal("test.int32", config.Adapters.TypeAdapters[0].Name);

            Cpp2CCodeGenerator generator = new(config);
            generator.Generate(header, output);

            Assert.True(generator.LastResult?.Success);
            string generated = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            Assert.Contains("API(int32_t) custom_add(int32_t left, int32_t right);", generated, StringComparison.Ordinal);
            Assert.Contains(generator.LastResult!.Module!.Functions, function => function.ManagedName == "custom_add");
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Registry_UsesPriorityThenStableNameAndRejectsDuplicates()
    {
        CppAdapterRegistry registry = new();
        registry.Register(new Int32TypeAdapter("z-low", 1));
        registry.Register(new Int32TypeAdapter("a-high", 2));

        Assert.Equal(new[] { "a-high", "z-low" }, registry.TypeAdapters.Select(adapter => adapter.Name));
        Assert.Throws<InvalidOperationException>(() => registry.Register(new Int32TypeAdapter("a-high", 9)));
        Assert.Equal(1, CppAdapterContract.CurrentVersion);
    }

    [Fact]
    public void CallableAdapter_RenamesConstructorsAndDestructorsAcrossEmissionAndIr()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-lifecycle-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "sample.hpp");
        string output = Path.Combine(temp, "GeneratedBridge");
        File.WriteAllText(header, "class Lifecycle { public: Lifecycle(int value); ~Lifecycle(); };\n");
        try
        {
            Cpp2CGeneratorConfig config = new();
            config.Adapters.Register(new LifecycleAdapter());
            Cpp2CCodeGenerator generator = new(config);

            generator.Generate(header, output);

            Assert.True(generator.LastResult?.Success);
            string generated = File.ReadAllText(Path.Combine(output, "include", "Classes.h"));
            Assert.Contains("custom_construct(int value)", generated, StringComparison.Ordinal);
            Assert.Contains("custom_release(Lifecycle* self)", generated, StringComparison.Ordinal);
            Assert.Contains(generator.LastResult!.Module!.Functions, function =>
                function.Kind == BindingFunctionKind.Constructor && function.ManagedName == "custom_construct");
            Assert.Contains(generator.LastResult.Module.Functions, function =>
                function.Kind == BindingFunctionKind.Destructor && function.ManagedName == "custom_release");
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void VersionOneAdapterContract_PublicInterfaceShapeIsLocked()
    {
        Assert.Equal(new[] { "Name", "Priority" }, typeof(ICppTypeAdapter).GetProperties()
            .Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(new[] { "CanAdapt", "CreatePlan" }, typeof(ICppTypeAdapter).GetMethods()
            .Where(method => !method.IsSpecialName).Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(new[] { "Name", "Priority" }, typeof(ICppCallableAdapter).GetProperties()
            .Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(new[] { "CanAdapt", "CreatePlan" }, typeof(ICppCallableAdapter).GetMethods()
            .Where(method => !method.IsSpecialName).Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ConfiguredPlugin_RegistersAdapterAndUsesStableCacheFingerprint()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "sample.hpp"), "int plugin_add(int left, int right) { return left + right; }\n");
        string configPath = Path.Combine(temp, "bridge.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new
        {
            EntryFiles = new[] { "sample.hpp" },
            OutputPath = "GeneratedBridge",
            PluginAssemblies = new[] { typeof(ConfiguredCppAdapterPlugin).Assembly.Location }
        }));
        try
        {
            Cpp2CCodeGenerator first = new(Cpp2CGeneratorConfig.Load(configPath));
            first.GenerateConfigured();
            Assert.True(first.LastResult?.Success);
            Assert.False(first.LastResult!.CacheHit);
            Assert.Contains("configured_plugin_add", File.ReadAllText(Path.Combine(temp, "GeneratedBridge", "include", "Classes.h")), StringComparison.Ordinal);

            Cpp2CCodeGenerator second = new(Cpp2CGeneratorConfig.Load(configPath));
            second.GenerateConfigured();
            Assert.True(second.LastResult?.Success);
            Assert.True(second.LastResult!.CacheHit);
            Assert.Equal(first.LastResult.CacheKey, second.LastResult.CacheKey);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private sealed class Int32TypeAdapter(string name = "test.int32", int priority = 100) : ICppTypeAdapter
    {
        public string Name { get; } = name;
        public int Priority { get; } = priority;

        public bool CanAdapt(CppType type, CppTypeAdapterContext context) =>
            type is CppPrimitiveType { Kind: CppPrimitiveKind.Int };

        public CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context) =>
            new(Name, CppTypeAdapterKind.Custom, "int32_t", MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
    }

    private sealed class RenameAddAdapter : ICppCallableAdapter
    {
        public string Name => "test.rename-add";
        public int Priority => 100;

        public bool CanAdapt(CppFunction function, CppCallableAdapterContext context) => function.Name == "add";

        public CppCallableAdapterPlan CreatePlan(CppFunction function, CppCallableAdapterContext context) =>
            new(Name, "custom_add");
    }

    private sealed class LifecycleAdapter : ICppCallableAdapter
    {
        public string Name => "test.lifecycle";
        public int Priority => 100;
        public bool CanAdapt(CppFunction function, CppCallableAdapterContext context) =>
            function.Flags.HasFlag(CppFunctionFlags.Constructor) || function.Flags.HasFlag(CppFunctionFlags.Destructor);
        public CppCallableAdapterPlan CreatePlan(CppFunction function, CppCallableAdapterContext context) =>
            new(Name, function.Flags.HasFlag(CppFunctionFlags.Constructor) ? "custom_construct" : "custom_release");
    }
}

public sealed class ConfiguredCppAdapterPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    public string Id => "bgcs.tests.cpp-adapter-plugin";
    public string Version => "1.0.0";
    public int ContractVersion => BindingPluginContract.CurrentVersion;
    public string GetCacheFingerprint() => "configured-cpp-adapter-v1";

    public void Configure(IBindingPluginHost host) =>
        host.Register<ICppCallableAdapter>("configured-plugin-callable", new CallableAdapter());

    private sealed class CallableAdapter : ICppCallableAdapter, ICacheFingerprintProvider
    {
        public string Name => "configured-plugin-callable";
        public int Priority => 100;
        public bool CanAdapt(CppFunction function, CppCallableAdapterContext context) => function.Name == "plugin_add";
        public CppCallableAdapterPlan CreatePlan(CppFunction function, CppCallableAdapterContext context) =>
            new(Name, "configured_plugin_add");
        public string GetCacheFingerprint() => "v1";
    }
}
