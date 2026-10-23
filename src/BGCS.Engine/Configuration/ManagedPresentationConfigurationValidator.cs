using System;
using System.Collections.Generic;
using BGCS.Core.Managed;

namespace BGCS.Configuration;

internal static class ManagedPresentationConfigurationValidator
{
    internal static void Validate(ApiConfiguration api)
    {
        if (api.importLibraryNameConstant is { } libraryConstant)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(libraryConstant);
            if (libraryConstant.Length > 2048) throw new ArgumentException("An import library constant identifier exceeds its bounded length.");
            if (api.importMode == BGCS.Core.BindingImportMode.FunctionTable)
                throw new ArgumentException("api.importLibraryNameConstant requires DllImport or LibraryImport; FunctionTable uses its module provider or Load argument.");
        }
        if (api.constants.IsDefault || api.nativeEnums.IsDefault)
            throw new ArgumentException("Managed constant and native enum presentation rules must be initialized.");
        HashSet<string> constants = new(StringComparer.Ordinal);
        foreach (ManagedConstantRuleRequest rule in api.constants)
        {
            ArgumentNullException.ThrowIfNull(rule);
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.constant);
            if (!constants.Add(rule.constant)) throw new ArgumentException("One native constant selector has multiple presentation rules.");
            if (rule.name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(rule.name);
            if (rule.managedType is not null) ArgumentException.ThrowIfNullOrWhiteSpace(rule.managedType);
            ValidateValue(rule.value, rule.expression);
        }
        HashSet<string> enums = new(StringComparer.Ordinal);
        foreach (ManagedNativeEnumRuleRequest rule in api.nativeEnums)
        {
            ArgumentNullException.ThrowIfNull(rule);
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.nativeType);
            if (!enums.Add(rule.nativeType)) throw new ArgumentException("One native enum selector has multiple presentation rules.");
            if (rule.name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(rule.name);
            if (rule.values.IsDefault) throw new ArgumentException("Native enum member presentation rules must be initialized.");
            HashSet<string> members = new(StringComparer.Ordinal);
            foreach (ManagedNativeEnumValueRuleRequest member in rule.values)
            {
                ArgumentNullException.ThrowIfNull(member);
                ArgumentException.ThrowIfNullOrWhiteSpace(member.member);
                if (!members.Add(member.member)) throw new ArgumentException("One native enum member selector has multiple presentation rules.");
                if (member.name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(member.name);
                ValidateValue(member.value, member.expression);
            }
        }
    }

    private static void ValidateValue(
        ManagedDefaultValue? value,
        string? expression
    ) {
        if (value is not null && expression is not null)
            throw new ArgumentException("A managed constant presentation must choose either a typed value or an expression.");
        if (value is not null && !Enum.IsDefined(value.kind))
            throw new ArgumentException("A managed constant presentation value kind is undefined.");
        if (expression is not null) ArgumentException.ThrowIfNullOrWhiteSpace(expression);
    }
}
