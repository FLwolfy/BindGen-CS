using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;

namespace BGCS.Analysis.Bridge;

/// <summary>Freezes exact opt-in family protocols without treating arbitrary template names as standard-library facts.</summary>
public static class CppFamilyBindingValidator
{
    /// <summary>Rejects ambiguous names, unsupported families and missing user assertions before native planning.</summary>
    /// <param name="bindings">The immutable configured collection.</param>
    /// <returns>A defensive normalized snapshot.</returns>
    public static ImmutableArray<CppFamilyBinding> Freeze(ImmutableArray<CppFamilyBinding> bindings)
    {
        if (bindings.IsDefault) throw new ArgumentException("C++ family bindings must be initialized.", nameof(bindings));
        HashSet<string> names = new(StringComparer.Ordinal);
        ImmutableArray<CppFamilyBinding>.Builder frozen = ImmutableArray.CreateBuilder<CppFamilyBinding>();
        foreach (CppFamilyBinding binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            bool template = binding.nativeTemplate is not null;
            if (template == (binding.nativeType is not null)) throw new ArgumentException("A C++ family binding requires exactly one nativeTemplate or nativeType selector.");
            string? selected = template ? binding.nativeTemplate : binding.nativeType;
            ArgumentException.ThrowIfNullOrWhiteSpace(selected);
            string configured = selected.Trim();
            if (!Regex.IsMatch(configured, "^(::)?[A-Za-z_][A-Za-z0-9_]*(::[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant))
                throw new ArgumentException("A C++ family binding requires one exact qualified template/type name without arguments or wildcards.");
            string name = NormalizeName(configured);
            if (!names.Add(name)) throw new InvalidOperationException("The same exact C++ family selector is bound more than once.");
            if (!Enum.IsDefined(binding.family)) throw new NotSupportedException("A configured C++ family must select a known finite value protocol.");
            if (!template && binding.family is not (StandardValueKind.String or StandardValueKind.Path))
                throw new ArgumentException("An exact non-template nativeType selector supports only the String or Path protocol.");
            if (binding.evidence is not { level: EvidenceLevel.UserAsserted } || string.IsNullOrWhiteSpace(binding.evidence.origin))
                throw new ArgumentException("A configured C++ family requires explicit UserAsserted protocol provenance; native compilation does not prove arbitrary ownership or behavioral semantics.");
            if (!Enum.IsDefined(binding.stringEncoding) || (binding.family == StandardValueKind.String
                ? binding.stringEncoding == NativeTextEncoding.Unknown : binding.stringEncoding != NativeTextEncoding.Unknown))
                throw new ArgumentException("A String family requires an explicit encoding; other families cannot carry a string encoding.");
            if (!Enum.IsDefined(binding.variantAccess) || (binding.family == StandardValueKind.Variant
                ? binding.variantAccess != CppVariantAccess.MemberGet : binding.variantAccess != CppVariantAccess.None))
                throw new ArgumentException("A configured Variant requires explicit MemberGet access; other families cannot carry variant access.");
            if (binding.family == StandardValueKind.Path ? binding.pathFormat is null || !Enum.IsDefined(binding.pathFormat.Value) : binding.pathFormat is not null)
                throw new ArgumentException("A configured Path requires an explicit known UTF-8 conversion; other families cannot carry path conversion.");
            if (!Enum.IsDefined(binding.stringAccess) || binding.family != StandardValueKind.String && binding.stringAccess != CppStringAccess.Indexed
                || (binding.cStr is not null) != (binding.family == StandardValueKind.String && binding.stringAccess == CppStringAccess.CStrNullTerminated))
                throw new ArgumentException("Only a CStrNullTerminated String protocol can carry a cStr contract.");
            CppCStrProtocol? cStr = binding.cStr;
            if (cStr is not null && (binding.stringEncoding != NativeTextEncoding.Utf8 || cStr.maximumCodeUnits <= 0
                || cStr.readableLifetime is not { kind: NativeLifetimeKind.Call, owner: null } lifetime || !Asserted(lifetime.evidence)
                || !Asserted(cStr.nullTerminated) || cStr.constructorCopiesInput is not null && !Asserted(cStr.constructorCopiesInput)))
                throw new ArgumentException("CStr access requires UTF-8, a positive scan bound and explicit UserAsserted call-readable termination/copy evidence.");
            frozen.Add(binding with { nativeTemplate = template ? name : null, nativeType = template ? null : name, evidence = new(EvidenceLevel.UserAsserted, binding.evidence.origin) });
        }
        return frozen.ToImmutable();

        static bool Asserted(Evidence? value) => value is { level: EvidenceLevel.UserAsserted } && !string.IsNullOrWhiteSpace(value.origin);
    }

    internal static string NormalizeName(string name)
    {
        name = name.TrimStart(':');
        if (name.StartsWith("std::__1::", StringComparison.Ordinal)) name = "std::" + name[10..];
        if (name.StartsWith("std::__cxx11::", StringComparison.Ordinal)) name = "std::" + name[14..];
        return name;
    }
}
