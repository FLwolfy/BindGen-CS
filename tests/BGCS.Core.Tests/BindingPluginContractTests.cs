using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Extensibility;
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

        Assert.Equal(BindingPluginContract.CurrentVersion, registry.ContractVersion);
        Assert.Equal(new[] { "a-high", "z-low" }, registry.GetServices<ITestService>().Select(value => value.Id));
        Assert.Throws<InvalidOperationException>(() =>
            registry.Register<ITestService>("a-high", new TestService("duplicate"), 99));
    }

    [Fact]
    public void PluginContract_PublicInterfaceShapeIsLocked()
    {
        Assert.True(BindingPluginContract.CurrentVersion > 0);
        Assert.Equal(new[] { "ContractVersion", "Id", "Version" },
            typeof(IBindingPlugin).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(new[] { "Configure" }, typeof(IBindingPlugin).GetMethods()
            .Where(method => !method.IsSpecialName).Select(method => method.Name));
        Assert.Equal(new[] { "ContractVersion" }, typeof(IBindingPluginHost).GetProperties().Select(property => property.Name));
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

        Assert.Contains(loaded, plugin => plugin.Id == AcceptancePlugin.PluginId);
        BindingPluginService<ICacheFingerprintProvider> service = Assert.Single(registry.GetServices<ICacheFingerprintProvider>());
        Assert.Equal("acceptance-probe", service.Id);
        Assert.Equal("loaded", service.Service.GetCacheFingerprint());
        Assert.True(registry.HasPlugins);
        Assert.Contains(AcceptancePlugin.PluginId, registry.GetCacheFingerprint(), StringComparison.Ordinal);
        Assert.Contains("acceptance-probe", registry.GetCacheFingerprint(), StringComparison.Ordinal);
        Assert.Contains("loaded", registry.GetCacheFingerprint(), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => BindingPluginLoader.Load(
            typeof(AcceptancePlugin).Assembly.Location,
            registry));
        Assert.Single(registry.GetServices<ICacheFingerprintProvider>());
    }

    private interface ITestService { }
    private sealed record TestService(string Value) : ITestService;
}

public sealed class AcceptancePlugin : IBindingPlugin
{
    public const string PluginId = "bgcs.tests.acceptance-plugin";

    public string Id => PluginId;
    public string Version => "1.0.0";
    public int ContractVersion => BindingPluginContract.CurrentVersion;

    public void Configure(IBindingPluginHost host) =>
        host.Register<ICacheFingerprintProvider>("acceptance-probe", new Probe());

    private sealed class Probe : ICacheFingerprintProvider
    {
        public string GetCacheFingerprint() => "loaded";
    }
}
