using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using BGCS.Analysis.Interop;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using Microsoft.CodeAnalysis.CSharp;

namespace BGCS.CSharp.Planning;

/// <summary>Plans Unicode literals or exact readonly native units without inventing an execution encoding.</summary>
public sealed class CSharpStringConstantPlanner
{
    /// <summary>Projects selected string identities using independently preserved literal categories and units.</summary>
    /// <param name="model">The immutable source constants and managed names.</param>
    /// <param name="cancellationToken">Cancels constant planning.</param>
    /// <returns>Exact inert C# declarations.</returns>
    public ImmutableArray<CSharpStringConstantPlan> Plan(
        ManagedApiModel model,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        if (model.interop.stringConstants.IsDefault || model.constants.IsDefault) throw new ArgumentException("String constant collections must be initialized.");
        var strings = model.interop.stringConstants.ToDictionary(constant => constant.sourceDeclaration);
        var scalars = model.interop.constants.Select(constant => constant.sourceDeclaration).ToHashSet();
        if (strings.Keys.Any(scalars.Contains)) throw new ArgumentException("One source constant cannot have scalar and string representations.");
        HashSet<StableId> identities = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        ImmutableArray<CSharpStringConstantPlan>.Builder result = ImmutableArray.CreateBuilder<CSharpStringConstantPlan>();
        foreach (ManagedConstant member in model.constants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = CSharpNames.Identifier(member.name);
            if (!identities.Add(member.sourceDeclaration) || !names.Add(name.TrimStart('@')))
                throw new ArgumentException("Managed scalar and string constants require unique identities and names.");
            if (!strings.TryGetValue(member.sourceDeclaration, out InteropStringConstant? native)) continue;
            if (name.TrimStart('@') is "Constants" or "GetType" or "GetHashCode" or "ToString" or "Equals" or "ReferenceEquals"
                || model.name.TrimStart('@') == "Constants") throw new ArgumentException("A string constant conflicts with its containing type.");
            new NativeStringConstantVerifier().Verify(native.value);
            string? decoded = Decode(native.value);
            string type = decoded is not null ? "string" : "global::System.ReadOnlySpan<" + (native.value.unitWidth == 1 ? "byte" : native.value.unitWidth == 2 ? "ushort" : "uint") + ">";
            string expression = decoded is not null ? SyntaxFactory.Literal(decoded).ToString()
                : "[" + string.Join(", ", native.value.codeUnits.Select(unit => "0x" + unit.ToString("X", CultureInfo.InvariantCulture) + (native.value.unitWidth == 4 ? "U" : string.Empty))) + "]";
            result.Add(new(member.sourceDeclaration, name, type, expression, decoded is not null)
            { documentation = native.documentation, unitWidth = native.value.unitWidth, nativeUnitCount = native.value.codeUnits.Length });
        }
        return result.ToImmutable();
    }

    private static string? Decode(NativeStringConstant value)
    {
        ReadOnlySpan<uint> units = value.codeUnits.AsSpan()[..^1];
        if (value.kind is NativeStringLiteralKind.Ordinary or NativeStringLiteralKind.Wide)
            return units.ToArray().All(unit => unit <= 127) ? new string(units.ToArray().Select(unit => (char)unit).ToArray()) : null;
        if (value.kind == NativeStringLiteralKind.Utf8)
        {
            try { return new UTF8Encoding(false, true).GetString(units.ToArray().Select(unit => (byte)unit).ToArray()); }
            catch (DecoderFallbackException) { return null; }
        }
        if (value.kind == NativeStringLiteralKind.Utf16) return new string(units.ToArray().Select(unit => (char)unit).ToArray());
        StringBuilder text = new();
        foreach (uint unit in units)
        {
            if (unit > 0x10FFFF || unit is >= 0xD800 and <= 0xDFFF) return null;
            text.Append(char.ConvertFromUtf32(checked((int)unit)));
        }
        return text.ToString();
    }
}

/// <summary>Describes one exact Unicode field or readonly native-code-unit property.</summary>
/// <param name="sourceDeclaration">The originating native identity.</param>
/// <param name="name">The checked managed identifier.</param>
/// <param name="type">The exact managed representation.</param>
/// <param name="expression">The inert escaped literal or code-unit initializer.</param>
/// <param name="isUnicodeString">Whether emission uses a C# string constant rather than an opaque unit property.</param>
public sealed record CSharpStringConstantPlan(
    StableId sourceDeclaration,
    string name,
    string type,
    string expression,
    bool isUnicodeString
)
{
    /// <summary>Gets detached documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets the original code-unit width.</summary>
    public int unitWidth { get; init; }
    /// <summary>Gets the complete unit count including the final terminator.</summary>
    public int nativeUnitCount { get; init; }
}
