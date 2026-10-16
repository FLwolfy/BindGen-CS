using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Contracts.Common;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Infrastructure.Plugins;
using BGCS.Tests.Fixtures.FailingFingerprintPlugin;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class NeutralPluginLoaderTests
{
    [Fact]
    public void PublicContractUsesFrozenDescriptorsTypedFactoriesAndCompleteFingerprintInputs()
    {
        Assert.Equal("descriptor", Assert.Single(typeof(IBindingPlugin).GetProperties()).Name);
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(IBindingPlugin)));
        MethodInfo configure = typeof(IBindingPlugin).GetMethod(nameof(IBindingPlugin.Configure))!;
        Assert.Equal(new[] { typeof(IExtensionRegistryBuilder), typeof(PluginConfiguration) }, configure.GetParameters().Select(parameter => parameter.ParameterType));
        MethodInfo register = Assert.Single(typeof(IExtensionRegistryBuilder).GetMethods());
        Assert.Equal("RegisterFactory", register.Name);
        Assert.True(register.IsGenericMethodDefinition);
        Assert.Equal(typeof(string), register.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(Func<>), register.GetParameters()[1].ParameterType.GetGenericTypeDefinition());
        MethodInfo capture = Assert.Single(typeof(ICacheFingerprintProvider).GetMethods());
        Assert.Equal(typeof(CacheFingerprintContribution), capture.ReturnType);
        Assert.Equal(new[] { typeof(CacheFingerprintContext), typeof(System.Threading.CancellationToken) }, capture.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(typeof(AcceptancePlugin).Assembly.GetReferencedAssemblies(), reference => reference.Name?.StartsWith("BGCS", StringComparison.Ordinal) == true && reference.Name != "BGCS.Contracts");
    }

    [Fact]
    public void ExternalEntrySharesExactContractsAndSamePathBytesRemainIndependentPerSession()
    {
        using Scratch scratch = new();
        using AcceptancePlugin metadata = new();
        LockedPluginManifest locked = scratch.Write(typeof(AcceptancePlugin), metadata.descriptor);
        PluginLoader loader = new();
        using IBindingPlugin first = loader.Activate(locked);
        using IBindingPlugin second = loader.Activate(locked);
        Registry firstRegistry = new(), secondRegistry = new();
        PluginConfiguration configuration = new(first.descriptor.id, JsonSerializer.SerializeToElement(new { }), scratch.root);
        first.Configure(firstRegistry, configuration);
        second.Configure(secondRegistry, configuration);
        INativeModuleTransform firstService = firstRegistry.Create(), secondService = secondRegistry.Create();
        Assert.Equal("acceptance-probe", firstService.id);
        Assert.Equal(typeof(INativeModuleTransform), firstService.GetType().GetInterface(typeof(INativeModuleTransform).FullName!));
        AssemblyLoadContext firstContext = AssemblyLoadContext.GetLoadContext(firstService.GetType().Assembly)!;
        AssemblyLoadContext secondContext = AssemblyLoadContext.GetLoadContext(secondService.GetType().Assembly)!;
        Assert.True(firstContext.IsCollectible);
        Assert.True(secondContext.IsCollectible);
        Assert.NotSame(firstContext, secondContext);
        Assert.NotSame(firstService, secondService);
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.descriptor);
        Assert.Equal(metadata.descriptor.id, second.descriptor.id);
    }

    [Fact]
    public void LoaderSharesTheCallingHostsContractsIdentityInsideAnIsolatedInfrastructureContext()
    {
        using Scratch scratch = new();
        using AcceptancePlugin metadata = new();
        LockedPluginManifest locked = scratch.Write(typeof(AcceptancePlugin), metadata.descriptor);
        IsolatedHost isolated = new(Path.GetDirectoryName(typeof(PluginLoader).Assembly.Location)!);
        try
        {
            Assembly infrastructure = isolated.LoadFromAssemblyPath(typeof(PluginLoader).Assembly.Location);
            Type reader = infrastructure.GetType(typeof(PluginManifestReader).FullName!, throwOnError: true)!;
            object privateManifest = reader.GetMethod(nameof(PluginManifestReader.Read))!.Invoke(Activator.CreateInstance(reader), [locked.path])!;
            Type loader = infrastructure.GetType(typeof(PluginLoader).FullName!, throwOnError: true)!;
            using IDisposable plugin = (IDisposable)loader.GetMethod(nameof(PluginLoader.Activate))!.Invoke(Activator.CreateInstance(loader), [privateManifest])!;
            Type contract = Assert.Single(plugin.GetType().GetInterfaces(), type => type.FullName == typeof(IBindingPlugin).FullName);
            Assert.Same(isolated, AssemblyLoadContext.GetLoadContext(contract.Assembly));
            Assert.NotSame(typeof(IBindingPlugin), contract);
            Assert.False(plugin is IBindingPlugin);
            object descriptor = contract.GetProperty(nameof(IBindingPlugin.descriptor))!.GetValue(plugin)!;
            Assert.Equal(metadata.descriptor.id, descriptor.GetType().GetProperty("id")!.GetValue(descriptor));
        }
        finally { isolated.Unload(); }
    }

    [Fact]
    public void ConstructorFailureDoesNotPoisonLaterIndependentActivationAtTheSameAssemblyPath()
    {
        using Scratch scratch = new();
        PluginDescriptor failing = new("bgcs.fixtures.constructor", "1.0.0", [new("constructor-probe", ExtensionPort.NativeModuleTransform)]);
        LockedPluginManifest locked = scratch.Write(typeof(ThrowingConstructorPlugin), failing);
        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => new PluginLoader().Activate(locked));
        Assert.Contains("constructor failed", Assert.IsType<InvalidOperationException>(exception.InnerException).Message, StringComparison.Ordinal);
        using AcceptancePlugin metadata = new();
        using IBindingPlugin plugin = new PluginLoader().Activate(scratch.Write(typeof(AcceptancePlugin), metadata.descriptor));
        Assert.Equal(metadata.descriptor.id, plugin.descriptor.id);
    }

    private sealed class Registry : IExtensionRegistryBuilder
    {
        private Func<INativeModuleTransform>? m_factory;
        public void RegisterFactory<TService>(string id, Func<TService> factory) where TService : class
        {
            Assert.Equal("acceptance-probe", id);
            Assert.Equal(typeof(INativeModuleTransform), typeof(TService));
            Assert.Null(m_factory);
            m_factory = () => (INativeModuleTransform)(object)factory();
        }
        internal INativeModuleTransform Create() => m_factory!();
    }

    private sealed class IsolatedHost(string directory) : AssemblyLoadContext("BGCS.Tests.IsolatedPluginHost:" + Guid.NewGuid().ToString("N"), isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
            => name.Name?.StartsWith("BGCS", StringComparison.Ordinal) == true
                ? LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll")) : null;
    }

    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-neutral-plugin-loader-" + Guid.NewGuid().ToString("N"));
        internal Scratch() => Directory.CreateDirectory(root);
        internal LockedPluginManifest Write(Type entry, PluginDescriptor descriptor)
        {
            byte[] bytes = File.ReadAllBytes(entry.Assembly.Location);
            const string assembly = "NeutralFixture.dll";
            File.WriteAllBytes(Path.Combine(root, assembly), bytes);
            PluginManifest manifest = new()
            {
                schemaVersion = 1, assembly = assembly, entryType = entry.FullName!, plugin = descriptor,
                assets = [new(assembly, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))))]
            };
            string path = Path.Combine(root, "plugin.json");
            File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }));
            return new PluginManifestReader().Read(path);
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
