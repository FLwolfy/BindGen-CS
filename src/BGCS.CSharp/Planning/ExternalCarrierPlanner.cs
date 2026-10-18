using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BGCS.Analysis.Layout;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Planning;

/// <summary>Plans typed public field copies while preserving the generated raw native ABI representation.</summary>
public sealed class ExternalCarrierPlanner
{
    /// <summary>Validates one public consumer field identifier without allowing arbitrary expressions.</summary>
    /// <param name="field">The exact consumer field name.</param>
    /// <returns>A checked C# field identifier.</returns>
    public static string NormalizeField(string field) => CSharpNames.Identifier(field);

    /// <summary>Validates a consumer type and normalizes a closed, globally qualified C# value-type reference.</summary>
    /// <param name="type">The requested closed type syntax.</param>
    /// <returns>Unambiguous consumer type syntax.</returns>
    public static string NormalizeType(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (type.Length > 4096) throw new ArgumentException("An external consumer type exceeds its syntax limit.", nameof(type));
        TypeSyntax syntax = SyntaxFactory.ParseTypeName(type, options: new CSharpParseOptions(LanguageVersion.CSharp14));
        if (syntax.ContainsDiagnostics || syntax.DescendantNodesAndSelf().Any(node => node is OmittedTypeArgumentSyntax
            or PointerTypeSyntax or FunctionPointerTypeSyntax or ArrayTypeSyntax or NullableTypeSyntax or TupleTypeSyntax
            or RefTypeSyntax) || syntax is not NameSyntax
            || syntax.DescendantTokens().Any(token => token.ValueText == "dynamic"))
            throw new ArgumentException("An external projection requires a closed named value type; pointers, arrays, nullable, tuple and open generic forms need explicit adapters.", nameof(type));
        string normalized = syntax.NormalizeWhitespace().ToFullString();
        return normalized.StartsWith("global::", StringComparison.Ordinal) ? normalized : "global::" + normalized;
    }

    /// <summary>Builds one checked pair of raw-to-consumer and consumer-to-raw field-copy helpers.</summary>
    /// <param name="module">The unchanged native ABI.</param>
    /// <param name="projection">The exact field mapping and static scalar shape or independently supplied storage observations.</param>
    /// <param name="rawCarrierType">The already planned generated raw carrier type.</param>
    /// <param name="rawFieldNames">Generated raw identifiers keyed by original native field identity.</param>
    /// <returns>The complete field-copy plan, with no change to the native import signature.</returns>
    public CSharpExternalTypeProjectionPlan Plan(
        InteropModule module,
        ExternalTypeProjection projection,
        string rawCarrierType,
        IReadOnlyDictionary<StableId, string> rawFieldNames
    ) {
        ArgumentNullException.ThrowIfNull(rawFieldNames);
        new ExternalTypeContractValidator().ValidateProjection(module, projection);
        string managed = NormalizeType(projection.managedType);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawCarrierType);
        TypeSyntax raw = SyntaxFactory.ParseTypeName(rawCarrierType, options: new CSharpParseOptions(LanguageVersion.CSharp14));
        if (raw.ContainsDiagnostics) throw new ArgumentException("The generated raw external projection carrier is not a valid C# type.");
        ImmutableArray<CSharpExternalFieldCopyPlan>.Builder fields = ImmutableArray.CreateBuilder<CSharpExternalFieldCopyPlan>();
        foreach (ExternalFieldProjection field in projection.fields)
        {
            if (!rawFieldNames.TryGetValue(field.nativeField, out string? rawName))
                throw new InvalidOperationException("An external projection's exact native field has no generated raw identifier.");
            ManagedScalarKind scalar = projection.managedShape is { } shape
                ? shape.fields.Single(item => item.name == field.managedField).scalarKind
                : projection.managedStorage!.fields.Single(item => item.name == field.managedField).scalarKind;
            fields.Add(new(field.nativeField, CSharpNames.Identifier(rawName), CSharpNames.Identifier(field.managedField))
            { isNativeAddress = scalar == ManagedScalarKind.NativeAddress });
        }
        string identity = projection.nativeType.value + "\n" + managed + "\n" + raw.NormalizeWhitespace().ToFullString() + "\n"
            + string.Join('\n', fields.Select(field => field.nativeField.value + "|" + field.rawField + "|" + field.managedField + "|" + field.isNativeAddress));
        string id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        return new(id, projection.nativeType, managed, raw.NormalizeWhitespace().ToFullString(), fields.ToImmutable());
    }
}

/// <summary>Plans exact field copies through independently checked generated native storage.</summary>
/// <param name="id">The deterministic helper identity.</param>
/// <param name="nativeType">The exact source record identity.</param>
/// <param name="managedType">The normalized closed consumer type.</param>
/// <param name="rawCarrierType">The unchanged generated native ABI carrier.</param>
/// <param name="fields">The exact source-to-consumer field copies.</param>
public sealed record CSharpExternalTypeProjectionPlan(
    string id,
    StableId nativeType,
    string managedType,
    string rawCarrierType,
    ImmutableArray<CSharpExternalFieldCopyPlan> fields
);

/// <summary>Names one typed assignment independently of declaration order and naming policy.</summary>
/// <param name="nativeField">The exact original field identity.</param>
/// <param name="rawField">The generated raw field identifier.</param>
/// <param name="managedField">The consumer field identifier.</param>
public sealed record CSharpExternalFieldCopyPlan(
    StableId nativeField,
    string rawField,
    string managedField
)
{
    /// <summary>Gets whether the exact scalar fields copy address bits without changing their ownership or pointee lifetime.</summary>
    public bool isNativeAddress { get; init; }
}
