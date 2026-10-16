using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BGCS.Composition;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Plugins;

namespace BGCS;

internal static class NativeManifestPluginComposition
{
    internal static FrozenExtensionRegistry Create(
        FrozenExtensionRegistry host,
        NativeBuildManifest manifest
    ) {
        BindingHostBuilder builder = BindingHostBuilder.FromFrozen(host, includePlugins: true);
        foreach (NativeManifestPluginActivation activation in manifest.plugins)
        {
            PluginRegistration? existing = host.Plugins.SingleOrDefault(plugin => plugin.Descriptor.id == activation.descriptor.id);
            if (existing is not null) SameDescriptor(existing.Descriptor, activation.descriptor);
            if (activation.manifestPath is string path)
            {
                LockedPluginManifest actual = new PluginManifestReader().Read(path);
                SameDescriptor(actual.manifest.plugin, activation.descriptor);
                SameInputs(actual, activation);
                if (existing is null) builder.AddPluginManifest(actual, activation.configuration.settings, activation.configuration.defaultPathBase);
                else
                {
                    if (existing.Manifest is null) throw new InvalidOperationException("A host factory registration cannot silently replace a content-locked portable native plugin: " + activation.descriptor.id);
                    SameInputs(existing.Manifest, activation);
                }
            }
            else if (existing is null)
                throw new InvalidOperationException("This native manifest requires an explicitly matching host SDK plugin registration: " + activation.descriptor.id);
            NativeManifestPluginSettings settings = activation.configuration;
            builder.WithPluginSettings(settings.id, settings.settings, settings.defaultPathBase, settings.valuePathBases);
        }
        return builder.Freeze();
    }
    private static void SameDescriptor(
        PluginDescriptor actual,
        PluginDescriptor expected
    ) {
        PluginDescriptorValidator.Freeze(actual);
        if (actual.id != expected.id || actual.version != expected.version || actual.contractsMajor != expected.contractsMajor || actual.minimumApiMinor != expected.minimumApiMinor
            || !actual.services.SequenceEqual(expected.services) || !JsonElement.DeepEquals(actual.configurationSchema, expected.configurationSchema))
            throw new InvalidOperationException("A native manifest plugin changed its exact compatibility, service or schema contract: " + expected.id);
    }
    private static void SameInputs(
        LockedPluginManifest actual,
        NativeManifestPluginActivation expected
    ) {
        StringComparer paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (!paths.Equals(actual.path, expected.manifestPath) || actual.inputs.Length != expected.inputs.Length || actual.missingInputs.Length != expected.missingInputs.Length
            || actual.inputs.Any(input => !expected.inputs.Any(locked => paths.Equals(input.path, locked.path) && input.length == locked.length && input.digest == locked.digest && paths.Equals(input.resolvedPath, locked.resolvedPath)))
            || actual.missingInputs.Any(input => !expected.missingInputs.Any(locked => paths.Equals(input, locked.path))))
            throw new IOException("The native manifest plugin executable closure changed after generation: " + expected.descriptor.id);
        new PluginManifestReader().Verify(actual);
    }
}
