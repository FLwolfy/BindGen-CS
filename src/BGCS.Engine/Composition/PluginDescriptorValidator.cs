using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Core.Plugins;
using BGCS.Infrastructure.Configuration;

namespace BGCS.Composition;

internal static class PluginDescriptorValidator
{
    internal static PluginDescriptor Freeze(PluginDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateId(descriptor.id);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.version);
        if (!Regex.IsMatch(descriptor.version, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\\.[0-9A-Za-z-]+)*)?(\\+[0-9A-Za-z-]+(\\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant))
            throw new ArgumentException("A plugin requires a semantic implementation version.");
        if (descriptor.contractsMajor != BGCS.Core.Versioning.ContractVersion.C_MAJOR
            || descriptor.minimumApiMinor < 0 || descriptor.minimumApiMinor > BGCS.Core.Versioning.ContractVersion.C_API_MINOR)
            throw new InvalidOperationException($"Plugin '{descriptor.id}' requires incompatible Contracts {descriptor.contractsMajor}.{descriptor.minimumApiMinor}; this host provides {BGCS.Core.Versioning.ContractVersion.C_MAJOR}.{BGCS.Core.Versioning.ContractVersion.C_API_MINOR}.");
        if (descriptor.services.IsDefaultOrEmpty) throw new ArgumentException("A plugin must declare its exact nonempty service collection.");
        HashSet<string> services = new(StringComparer.Ordinal);
        foreach (ExtensionDescriptor service in descriptor.services)
        {
            ArgumentNullException.ThrowIfNull(service);
            ValidateId(service.id);
            if (!Enum.IsDefined(service.port) || !services.Add(service.id)) throw new InvalidOperationException("A plugin contains an unknown port or duplicate global service identity.");
        }
        new ExtensionSchemaValidator().ValidateSchema(descriptor.configurationSchema);
        return descriptor with { services = descriptor.services.ToArray().ToImmutableArray(), configurationSchema = descriptor.configurationSchema.Clone() };
    }

    internal static void ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._/-]*$", RegexOptions.CultureInvariant)) throw new ArgumentException("An extension identity must use stable ASCII letters, digits, dots, slashes, underscores or hyphens.");
    }
}
