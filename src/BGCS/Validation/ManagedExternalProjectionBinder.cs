using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;

namespace BGCS.Validation;

internal static class ManagedExternalProjectionBinder
{
    internal static async Task<(ManagedApiModel Model, SourceSnapshot? Inputs)> BindAsync(
        ManagedApiModel model,
        BindingConfiguration configuration,
        string baseDirectory,
        CancellationToken cancellationToken
    ) {
        ImmutableArray<ExternalTypeProjection>.Builder projections = model.externalTypeProjections.ToBuilder();
        HashSet<StableId> selected = model.externalTypeProjections.Select(item => item.nativeType).ToHashSet();
        foreach (ExternalTypeConfiguration configured in configuration.api.externalTypes)
        {
            InteropType[] matches = model.interop.types.Where(type => type.category == NativeTypeCategory.Record
                && (type.id.value == configured.nativeType || type.sourceQualifiedName == configured.nativeType
                    || type.declarationName == configured.nativeType || type.nativeName == configured.nativeType)).ToArray();
            if (matches.Length != 1) throw new ArgumentException("An external projection must select exactly one native record: " + configured.nativeType);
            InteropType record = matches[0];
            if (!selected.Add(record.id)) throw new ArgumentException("One native record has multiple external consumer projections.");
            ImmutableArray<ExternalFieldProjection>.Builder fields = ImmutableArray.CreateBuilder<ExternalFieldProjection>();
            foreach ((string nativeName, string managedName) in configured.fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                InteropField[] matchingFields = record.fields.Where(field => field.name == nativeName || field.id.value == nativeName).ToArray();
                if (matchingFields.Length != 1) throw new ArgumentException("An external projection must select one exact native field: " + nativeName);
                fields.Add(new(matchingFields[0].id, managedName));
            }
            projections.Add(new(record.id, configured.managedType, fields.ToImmutable()));
        }
        if (projections.Count == 0) return (model, null);
        if (projections.Select(item => item.nativeType).Distinct().Count() != projections.Count)
            throw new ArgumentException("An external projection is registered more than once for one native type.");
        ManagedExternalTypeProbeResult observed = await new ManagedExternalTypeProbe().ProbeAsync(projections.ToImmutable(),
            configuration.managedBuild, baseDirectory, TimeSpan.FromSeconds(configuration.execution.managedCompilationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        return (model with
        {
            externalTypeProjections = projections.Select((
                item,
                index
            ) => item with { managedStorage = observed.facts[index] }).ToImmutableArray()
        }, observed.inputs);
    }
}
