using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;

namespace BGCS.CSharp.Projection;

internal static class ManagedRecordCustomization
{
    internal static ManagedApiModel Apply(
        ManagedApiModel model,
        ImmutableArray<ManagedRecordRuleRequest> rules
    ) {
        if (rules.IsDefault) throw new ArgumentException("Record presentation rules must be initialized.");
        HashSet<StableId> customized = [];
        var types = model.types.ToBuilder();
        foreach (ManagedRecordRuleRequest rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.nativeType);
            InteropType[] selected = model.interop.types.Where(type => type.id.value == rule.nativeType || type.sourceNativeType?.value == rule.nativeType
                || type.sourceQualifiedName == rule.nativeType || type.declarationName == rule.nativeType || type.nativeName == rule.nativeType)
                .Select(Resolve).Where(type => type.category == NativeTypeCategory.Record).DistinctBy(type => type.id).ToArray();
            if (selected.Length != 1) throw new ArgumentException("A record presentation must identify exactly one native record: " + rule.nativeType);
            InteropType record = selected[0];
            if (!customized.Add(record.id)) throw new ArgumentException("A native record has multiple presentation rules.");
            if (rule.fields.IsDefault || rule.constructorFields.IsDefault) throw new ArgumentException("Record field rules must be initialized.");
            int index = Enumerable.Range(0, types.Count).Single(index => types[index].nativeType == record.id);
            ManagedType managed = types[index];
            var names = managed.memberNames.ToDictionary(name => name.nativeDeclaration);
            HashSet<StableId> fields = [];
            var documentation = ImmutableArray.CreateBuilder<ManagedFieldDocumentation>();
            foreach (ManagedFieldRuleRequest field in rule.fields)
            {
                ArgumentNullException.ThrowIfNull(field);
                InteropField source = Field(field.field);
                if (!fields.Add(source.id)) throw new ArgumentException("A native field has multiple presentation rules.");
                if (field.name is not null) { ArgumentException.ThrowIfNullOrWhiteSpace(field.name); names[source.id] = new(source.id, field.name); }
                if (field.documentation is not null) documentation.Add(new(source.id, field.documentation));
            }
            ImmutableArray<StableId> constructor = rule.constructorFields.Select(name => Field(name).id).ToImmutableArray();
            if (constructor.Distinct().Count() != constructor.Length) throw new ArgumentException("A carrier constructor cannot assign one native field more than once.");
            ManagedRecordValidity? validity = rule.validity is not { } requested ? null
                : new(Field(requested.field).id, requested.invalidValue, requested.propertyName);
            if (rule.name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(rule.name);
            types[index] = managed with
            {
                name = rule.name ?? managed.name,
                memberNames = names.Values.OrderBy(name => name.nativeDeclaration.value, StringComparer.Ordinal).ToImmutableArray(),
                recordPresentation = new()
                {
                    documentation = rule.documentation,
                    fieldDocumentation = documentation.ToImmutable(),
                    emitSizeConstant = rule.emitSizeConstant,
                    constructorFields = constructor,
                    validity = validity
                }
            };

            InteropField Field(string selector)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(selector);
                InteropField[] candidates = record.fields.Where(field => field.id.value == selector || field.name == selector).ToArray();
                if (candidates.Length != 1) throw new ArgumentException("A record field selector must identify exactly one native field: " + selector);
                return candidates[0];
            }
        }
        return model with { types = types.ToImmutable() };

        InteropType Resolve(InteropType type)
        {
            HashSet<StableId> visited = [];
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is StableId element && visited.Add(type.id))
                type = model.interop.GetType(element);
            return type;
        }
    }
}
