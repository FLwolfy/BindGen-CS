using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using BGCS.Core.Extensibility;
using BGCS.Tests.Fixtures.FailingFingerprintPlugin;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class BindingPluginContractTests
{
    [Fact]
    public void Registry_OrdersTypedServicesAndRejectsIdentityConflicts()
    {
        BindingPluginRegistry registry = new();
        registry.Register<ITestService>("z-low", new TestService("low"), 1);
        registry.Register<ITestService>("a-high", new TestService("high"), 10);

        Assert.Equal(BindingPluginContract.C_CURRENT_VERSION, registry.contractVersion);
        Assert.Equal(new[] { "a-high", "z-low" }, registry.GetServices<ITestService>().Select(value => value.id));
        Assert.Throws<InvalidOperationException>(() =>
            registry.Register<ITestService>("a-high", new TestService("duplicate"), 99));
    }

    [Fact]
    public void PluginContract_PublicInterfaceShapeIsLocked()
    {
        Assert.True(BindingPluginContract.C_CURRENT_VERSION > 0);
        Assert.Equal(new[] { "contractVersion", "id", "version" },
            typeof(IBindingPlugin).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(new[] { "Configure" }, typeof(IBindingPlugin).GetMethods()
            .Where(method => !method.IsSpecialName).Select(method => method.Name));
        Assert.Equal(new[] { "contractVersion" }, typeof(IBindingPluginHost).GetProperties().Select(property => property.Name));
        Assert.Equal(new[] { "Register" }, typeof(IBindingPluginHost).GetMethods()
            .Where(method => !method.IsSpecialName).Select(method => method.Name));
        Assert.Equal(new[] { "GetCacheFingerprint" }, typeof(ICacheFingerprintProvider).GetMethods()
            .Where(method => !method.IsSpecialName).Select(method => method.Name));
    }

    [Fact]
    public void Loader_ActivatesPublicPluginFromExternalAssembly()
    {
        BindingPluginRegistry registry = new();

        IReadOnlyList<IBindingPlugin> loaded = BindingPluginLoader.Load(
            typeof(AcceptancePlugin).Assembly.Location,
            registry);

        Assert.Contains(loaded, plugin => plugin.id == AcceptancePlugin.PluginId);
        BindingPluginService<ICacheFingerprintProvider> service = Assert.Single(registry.GetServices<ICacheFingerprintProvider>());
        Assert.Equal("acceptance-probe", service.id);
        Assert.Equal("loaded", service.service.GetCacheFingerprint());
        Assert.True(registry.hasPlugins);
        Assert.Contains(AcceptancePlugin.PluginId, registry.GetCacheFingerprint(), StringComparison.Ordinal);
        Assert.Contains("acceptance-probe", registry.GetCacheFingerprint(), StringComparison.Ordinal);
        Assert.Contains("loaded", registry.GetCacheFingerprint(), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => BindingPluginLoader.Load(
            typeof(AcceptancePlugin).Assembly.Location,
            registry));
        Assert.Single(registry.GetServices<ICacheFingerprintProvider>());
    }

    [Fact]
    public void Loader_SharesContractsWithAnIsolatedGeneratorHost()
    {
        // Public reflection models a task host whose contract types cannot be cast into Default.
        var host = new AssemblyLoadContext("BGCS.Tests.IsolatedGenerator:" + Guid.NewGuid(), isCollectible: false);
        Assembly core = host.LoadFromAssemblyPath(typeof(BindingPluginLoader).Assembly.Location);
        Type registryType = core.GetType(typeof(BindingPluginRegistry).FullName!, throwOnError: true)!;
        object registry = Activator.CreateInstance(registryType)!;
        Type loaderType = core.GetType(typeof(BindingPluginLoader).FullName!, throwOnError: true)!;

        MethodInfo load = loaderType.GetMethod(nameof(BindingPluginLoader.Load), BindingFlags.Public | BindingFlags.Static)!;
        object loaded = load.Invoke(null, [typeof(AcceptancePlugin).Assembly.Location, registry])!;

        Assert.NotNull(loaded);
        Assert.Equal(true, registryType.GetProperty(nameof(BindingPluginRegistry.hasPlugins))!.GetValue(registry));
    }

    [Fact]
    public void Loader_FingerprintFailureLeavesTheLiveRegistryUnchanged()
    {
        BindingPluginRegistry registry = new();
        registry.Register<ITestService>("existing", new TestService("retained"));
        IReadOnlyList<BindingPluginService<ITestService>> before = registry.GetServices<ITestService>();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                BindingPluginLoader.Load(typeof(RejectedFingerprintPlugin).Assembly.Location, registry));
            Assert.Contains("cannot produce a fingerprint", failure.Message, StringComparison.Ordinal);
            Assert.Equal(1, registry.count);
            Assert.False(registry.hasPlugins);
            Assert.Empty(registry.GetCacheFingerprint());
            Assert.Empty(registry.GetServices<ICacheFingerprintProvider>());
            Assert.Same(before[0].service, Assert.Single(registry.GetServices<ITestService>()).service);
        }
    }

    [Fact]
    public void Loader_ServiceConflictDoesNotPublishPluginIdentityOrFingerprint()
    {
        BindingPluginRegistry registry = new();
        var existing = new ExistingFingerprint();
        registry.Register<ICacheFingerprintProvider>("acceptance-probe", existing);

        Assert.Throws<InvalidOperationException>(() =>
            BindingPluginLoader.Load(typeof(AcceptancePlugin).Assembly.Location, registry));

        Assert.False(registry.hasPlugins);
        Assert.Empty(registry.GetCacheFingerprint());
        Assert.Same(existing, Assert.Single(registry.GetServices<ICacheFingerprintProvider>()).service);
    }

    private sealed class ExistingFingerprint : ICacheFingerprintProvider
    {
        public string GetCacheFingerprint() => "existing";
    }

    private interface ITestService { }
    private sealed record TestService(string Value) : ITestService;
}

public sealed class AcceptancePlugin : IBindingPlugin
{
    public const string PluginId = "bgcs.tests.acceptance-plugin";

    public string id => PluginId;
    public string version => "1.0.0";
    public int contractVersion => BindingPluginContract.C_CURRENT_VERSION;

    public void Configure(IBindingPluginHost host) =>
        host.Register<ICacheFingerprintProvider>("acceptance-probe", new Probe());

    private sealed class Probe : ICacheFingerprintProvider
    {
        public string GetCacheFingerprint() => "loaded";
    }
}
