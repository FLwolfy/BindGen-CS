using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Checks explicit native source identities and dependencies without claiming a security sandbox.</summary>
public sealed class ControlledSourceFragmentVerifier
{
    /// <summary>Validates exact source bytes, finite declared placeholders and detached dependency identities.</summary>
    /// <param name="fragment">The immutable native source fragment.</param>
    /// <param name="source">The originating native graph.</param>
    /// <param name="headers">The allowed parsing and emitted bridge header inputs.</param>
    /// <param name="placeholders">Allowed expression substitution identities; empty for translation-unit declarations.</param>
    public void Validate(
        ControlledSourceFragment fragment,
        NativeModule source,
        IEnumerable<string> headers,
        IEnumerable<string> placeholders
    ) {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragment.origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragment.text);
        if (!Enum.IsDefined(fragment.kind) || fragment.text.Length > 1024 * 1024 || fragment.text.Contains('\0'))
            throw new ArgumentException("A native source fragment has an unknown placement, invalid text or exceeds the 1 MiB limit.", nameof(fragment));
        if (fragment.digest.value != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fragment.text))))
            throw new InvalidOperationException("The native source fragment changed after its content identity was declared.");
        if (fragment.evidence.level is not (EvidenceLevel.UserAsserted or EvidenceLevel.Declared))
            throw new InvalidOperationException("A native source escape hatch must identify its explicit assertion; it cannot declare independent verification.");
        if (fragment.declarationDependencies.IsDefault || fragment.typeDependencies.IsDefault || fragment.requiredHeaders.IsDefault)
            throw new ArgumentException("Native source fragment dependency collections must be initialized immutable arrays.", nameof(fragment));
        foreach (StableId declaration in fragment.declarationDependencies) source.GetDeclaration(declaration);
        foreach (StableId type in fragment.typeDependencies) source.GetType(type);
        HashSet<string> inputs = headers.ToHashSet(StringComparer.Ordinal);
        if (fragment.requiredHeaders.Any(header => !inputs.Contains(header)))
            throw new InvalidOperationException("A native source fragment references a header outside its declared parse and build inputs.");
        HashSet<string> allowed = placeholders.ToHashSet(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(fragment.text, "\\{([A-Za-z][A-Za-z0-9]*)\\}", RegexOptions.CultureInvariant))
            if (fragment.kind == SourceFragmentKind.Expression && !allowed.Contains(match.Groups[1].Value))
                throw new InvalidOperationException("A native expression contains an undeclared substitution placeholder.");
    }

    /// <summary>Substitutes only exact previously validated placeholders in an expression.</summary>
    /// <param name="fragment">The validated expression.</param>
    /// <param name="values">The finite source expressions by placeholder identity.</param>
    /// <returns>The native expression with declared substitutions applied once.</returns>
    public string Substitute(
        ControlledSourceFragment fragment,
        IReadOnlyDictionary<string, string> values
    ) {
        if (fragment.kind != SourceFragmentKind.Expression) throw new ArgumentException("Only native expressions permit substitution.", nameof(fragment));
        return Regex.Replace(fragment.text, "\\{([A-Za-z][A-Za-z0-9]*)\\}", match => values.TryGetValue(match.Groups[1].Value, out string? value)
            ? value : throw new InvalidOperationException("A native expression omitted one declared substitution."), RegexOptions.CultureInvariant);
    }
}
