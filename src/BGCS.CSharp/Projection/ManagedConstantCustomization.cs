using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;

namespace BGCS.CSharp.Projection;

internal static class ManagedConstantCustomization
{
    internal static ManagedApiModel Apply(
        ManagedApiModel model,
        ManagedProjectionRequest request
    ) {
        if (request.constants.IsDefault || request.nativeEnums.IsDefault)
            throw new ArgumentException("Constant and native enum presentation rules must be initialized.");
        var constants = model.constants.ToBuilder();
        var types = model.types.ToBuilder();
        HashSet<StableId> changedConstants = [];
        var sources = model.interop.constants.Select(value => (value.sourceDeclaration, value.name, value.qualifiedName))
            .Concat(model.interop.stringConstants.Select(value => (value.sourceDeclaration, value.name, value.qualifiedName))).ToArray();
        foreach (ManagedConstantRuleRequest rule in request.constants)
        {
            ArgumentNullException.ThrowIfNull(rule);
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.constant);
            var selected = sources.Where(value => value.sourceDeclaration.value == rule.constant || value.name == rule.constant
                || value.qualifiedName.TrimStart(':') == rule.constant.TrimStart(':')).ToArray();
            if (selected.Length != 1) throw new ArgumentException("A constant presentation must select exactly one existing native constant: " + rule.constant);
            StableId identity = selected[0].sourceDeclaration;
            if (!changedConstants.Add(identity)) throw new ArgumentException("A native constant has multiple presentation rules.");
            int index = Enumerable.Range(0, constants.Count).Single(index => constants[index].sourceDeclaration == identity);
            if (rule.name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(rule.name);
            ValidateValue(rule.value, rule.expression);
            if (rule.managedType is not null) ArgumentException.ThrowIfNullOrWhiteSpace(rule.managedType);
            constants[index] = constants[index] with
            {
                name = rule.name ?? constants[index].name,
                presentation = new(new(EvidenceLevel.UserAsserted, "api.constants:" + identity.value))
                { documentation = rule.documentation, managedType = rule.managedType, value = rule.value, expression = rule.expression }
            };
        }
        HashSet<StableId> changedEnums = [];
        foreach (ManagedNativeEnumRuleRequest rule in request.nativeEnums)
        {
            ArgumentNullException.ThrowIfNull(rule);
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.nativeType);
            InteropType[] selected = model.interop.types.Where(type => type.id.value == rule.nativeType || type.sourceNativeType?.value == rule.nativeType
                || type.nativeName == rule.nativeType || type.declarationName == rule.nativeType || type.sourceQualifiedName == rule.nativeType)
                .Select(Resolve).Where(type => type.category == NativeTypeCategory.Enumeration).DistinctBy(type => type.id).ToArray();
            if (selected.Length != 1) throw new ArgumentException("A native enum presentation must select exactly one existing native enum: " + rule.nativeType);
            InteropType native = selected[0];
            if (!changedEnums.Add(native.id)) throw new ArgumentException("A native enum has multiple presentation rules.");
            if (rule.values.IsDefault) throw new ArgumentException("Native enum member presentation rules must be initialized.");
            int index = Enumerable.Range(0, types.Count).Single(index => types[index].nativeType == native.id);
            ManagedType type = types[index];
            var names = type.memberNames.ToDictionary(item => item.nativeDeclaration);
            HashSet<StableId> changedMembers = [];
            var values = ImmutableArray.CreateBuilder<ManagedNativeEnumValuePresentation>();
            foreach (ManagedNativeEnumValueRuleRequest item in rule.values)
            {
                ArgumentNullException.ThrowIfNull(item);
                ArgumentException.ThrowIfNullOrWhiteSpace(item.member);
                var members = native.enumValues.Where(value => value.id.value == item.member || value.name == item.member
                    || (native.sourceQualifiedName ?? native.declarationName ?? native.nativeName).TrimStart(':') + "::" + value.name == item.member.TrimStart(':')).ToArray();
                if (members.Length != 1) throw new ArgumentException("An enum value presentation must select exactly one existing native member: " + item.member);
                var member = members[0];
                if (!changedMembers.Add(member.id)) throw new ArgumentException("A native enum member has multiple presentation rules.");
                ValidateValue(item.value, item.expression);
                if (item.name is not null) { ArgumentException.ThrowIfNullOrWhiteSpace(item.name); names[member.id] = new(member.id, item.name); }
                values.Add(new(member.id, new(new(EvidenceLevel.UserAsserted, "api.nativeEnums:" + native.id.value + ":" + member.id.value))
                { documentation = item.documentation, value = item.value, expression = item.expression }));
            }
            if (rule.name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(rule.name);
            types[index] = type with
            {
                name = rule.name ?? type.name,
                isNameExplicit = rule.name is not null || type.isNameExplicit,
                memberNames = names.Values.OrderBy(value => value.nativeDeclaration.value, StringComparer.Ordinal).ToImmutableArray(),
                enumPresentation = new(values.ToImmutable()) { documentation = rule.documentation }
            };
        }
        return model with
        {
            constants = constants.ToImmutable(),
            types = types.ToImmutable(),
            emitLibraryNameConstant = request.emitLibraryNameConstant,
            importLibraryNameConstant = request.importLibraryNameConstant
        };

        InteropType Resolve(InteropType type)
        {
            HashSet<StableId> seen = [];
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is StableId element && seen.Add(type.id))
                type = model.interop.GetType(element);
            return type;
        }
    }

    private static void ValidateValue(
        ManagedDefaultValue? value,
        string? expression
    ) {
        if (value is not null && expression is not null) throw new ArgumentException("A constant value and compile-time expression are mutually exclusive.");
        if (value is not null && !Enum.IsDefined(value.kind)) throw new ArgumentException("A constant assertion kind is undefined.");
        if (expression is not null && (string.IsNullOrWhiteSpace(expression) || expression.Length > 16 * 1024))
            throw new ArgumentException("A constant expression must contain between one and 16384 characters.");
    }
}
