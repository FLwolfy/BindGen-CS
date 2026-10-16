using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Managed;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Projection;

/// <summary>Validates explicit managed recipe syntax even when the native matching rule is unused.</summary>
public sealed class ManagedRecipeProjectionValidator
{
    /// <summary>Checks all configured projection types and both conversion expressions independently of target selection.</summary>
    /// <param name="recipes">All declared neutral native recipes.</param>
    public void ValidateRecipes(IEnumerable<TypeLoweringRecipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (TypeLoweringRecipe recipe in recipes)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            ArgumentException.ThrowIfNullOrWhiteSpace(recipe.id);
            if (!ids.Add(recipe.id)) throw new InvalidOperationException("A managed recipe identity is registered more than once.");
            if (recipe.managedProjection is not null) Plan(recipe.managedProjection, recipe.cAbiType, 8);
        }
    }

    /// <summary>Checks a managed projection and resolves native-size carrier aliases using the parsed target pointer width.</summary>
    /// <param name="projection">The public type and explicit conversion assertions.</param>
    /// <param name="carrier">The exact C# raw carrier or configured fixed-width C carrier spelling.</param>
    /// <param name="pointerSize">The parsed source target's pointer byte width.</param>
    /// <returns>A source-backend plan with normalized type and expression syntax.</returns>
    public CSharpRecipeProjectionPlan Plan(
        ManagedRecipeProjection projection,
        string carrier,
        int pointerSize
    ) {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.managedType);
        if (pointerSize is not (4 or 8)) throw new NotSupportedException("Managed native recipes require an actual supported pointer ABI.");
        TypeSyntax managed = SyntaxFactory.ParseTypeName(projection.managedType, options: new CSharpParseOptions(LanguageVersion.CSharp14));
        if (managed.ContainsDiagnostics || managed.IsKind(SyntaxKind.PredefinedType) && ((PredefinedTypeSyntax)managed).Keyword.IsKind(SyntaxKind.VoidKeyword))
            throw new InvalidOperationException("A managed recipe type is syntactically invalid or cannot represent a value.");
        string? requiredNamespace = projection.requiredNamespace is null ? null : CSharpNames.Namespace(projection.requiredNamespace);
        if (requiredNamespace is not null) managed = Qualify(managed, requiredNamespace);
        string native = Carrier(carrier, pointerSize);
        string? toNative = Expression(projection.managedToNativeExpression);
        string? fromNative = Expression(projection.nativeToManagedExpression);
        if (toNative is null && fromNative is null) throw new InvalidOperationException("A managed recipe projection must declare an input or output conversion.");
        string id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { managed.ToString(), native, toNative, fromNative, requiredNamespace }))))[..16];
        return new(id, managed.NormalizeWhitespace().ToFullString(), native, toNative, fromNative, requiredNamespace);
    }

    private static TypeSyntax Qualify(
        TypeSyntax type,
        string requiredNamespace
    ) => type switch
    {
        IdentifierNameSyntax or GenericNameSyntax => SyntaxFactory.ParseTypeName("global::" + requiredNamespace + "." + type),
        NullableTypeSyntax nullable => nullable.WithElementType(Qualify(nullable.ElementType, requiredNamespace)),
        ArrayTypeSyntax array => array.WithElementType(Qualify(array.ElementType, requiredNamespace)),
        PointerTypeSyntax pointer => pointer.WithElementType(Qualify(pointer.ElementType, requiredNamespace)),
        _ => type
    };

    private static string? Expression(string? text)
    {
        if (text is null) return null;
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > 1024 * 1024 || text.Contains('\0')) throw new InvalidOperationException("A managed conversion exceeds its text limit or contains a null character.");
        var literalSpans = SyntaxFactory.ParseTokens(text).Where(token => token.Kind().ToString().Contains("String", StringComparison.Ordinal)
            || token.IsKind(SyntaxKind.CharacterLiteralToken)).Select(token => token.Span).ToArray();
        string source = Regex.Replace(text, "\\{([A-Za-z][A-Za-z0-9]*)\\}", match =>
        {
            if (literalSpans.Any(span => span.Contains(match.Index))) return match.Value;
            if (match.Groups[1].Value != "value") throw new InvalidOperationException("A managed conversion contains an undeclared placeholder.");
            return "value";
        }, RegexOptions.CultureInvariant);
        ExpressionSyntax expression = SyntaxFactory.ParseExpression(source, options: new CSharpParseOptions(LanguageVersion.CSharp14), consumeFullText: true);
        if (expression.ContainsDiagnostics) throw new InvalidOperationException("A managed conversion expression is syntactically invalid: " + source);
        return expression.NormalizeWhitespace().ToFullString();
    }

    private static string Carrier(
        string value,
        int pointerSize
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string carrier = value.Trim();
        if (carrier.EndsWith('*'))
        {
            string element = carrier[..^1].Trim();
            if (element.StartsWith("const ", StringComparison.Ordinal)) element = element[6..].Trim();
            return Carrier(element, pointerSize) + "*";
        }
        return carrier switch
        {
            "int8_t" => "sbyte",
            "uint8_t" => "byte",
            "int16_t" => "short",
            "uint16_t" => "ushort",
            "int32_t" => "int",
            "uint32_t" => "uint",
            "int64_t" => "long",
            "uint64_t" => "ulong",
            "size_t" or "uintptr_t" => pointerSize == 4 ? "uint" : "ulong",
            "ptrdiff_t" or "intptr_t" => pointerSize == 4 ? "int" : "long",
            _ => carrier
        };
    }
}
