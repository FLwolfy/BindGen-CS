using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using BGCS.Contracts.Managed;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Planning;

internal static class ManagedDefaultValuePlanner
{
    internal static string Expression(
        ManagedDefaultValue value,
        string type,
        CSharpEmissionPlan plan
    ) {
        ArgumentNullException.ThrowIfNull(value);
        string scalarType = Canonical(type);
        if (!Enum.IsDefined(value.kind)) throw new ArgumentException("A default argument kind is undefined.");
        if (value.kind is ManagedDefaultValueKind.Null or ManagedDefaultValueKind.Default)
        {
            if (value.value is not null) throw new ArgumentException("Null and default values cannot contain literal text.");
            if (value.kind == ManagedDefaultValueKind.Null && !type.EndsWith('*') && scalarType != "string?")
                throw new ArgumentException("A null default requires a nullable friendly string or raw pointer.");
            return value.kind == ManagedDefaultValueKind.Null ? "null" : "default(" + type + ")";
        }
        string text = value.value ?? throw new ArgumentException("The selected default argument requires an explicit value.");
        switch (value.kind)
        {
            case ManagedDefaultValueKind.Boolean:
                if (scalarType != "bool" || text is not ("true" or "false")) throw new ArgumentException("A Boolean default requires an actual Boolean friendly input.");
                return text;
            case ManagedDefaultValueKind.String:
                if (scalarType is not ("string" or "string?")) throw new ArgumentException("A Unicode string default requires a string-friendly input.");
                return SymbolDisplay.FormatLiteral(text, true);
            case ManagedDefaultValueKind.Character:
                if (scalarType != "char" || text.Length != 1) throw new ArgumentException("A character default requires one UTF-16 code unit and a character input.");
                return SymbolDisplay.FormatLiteral(text[0], true);
            case ManagedDefaultValueKind.ConstantReference:
                NameSyntax name = SyntaxFactory.ParseName(text);
                if (name.ContainsDiagnostics || name.DescendantNodesAndSelf().Any(node => node is GenericNameSyntax)
                    || name.ToFullString() != text || !name.DescendantTokens().Any())
                    throw new ArgumentException("A constant reference requires a qualified identifier, not an invocation or statement.");
                return text;
            case ManagedDefaultValueKind.Integer:
                if (!BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger integer))
                    throw new ArgumentException("An integer default requires an exact base-ten value.");
                string integral = scalarType;
                bool Matches(string name) => type.TrimStart('@') == name.TrimStart('@') || type == CSharpGeneratedTypeScope.QualifiedReference(plan.api, name);
                CSharpManagedEnumPlan? managedEnum = plan.managedEnums.SingleOrDefault(item => Matches(item.name));
                if (managedEnum is not null) integral = managedEnum.underlyingType;
                else if (plan.storage.SingleOrDefault(item => Matches(item.name))?.enumUnderlyingType is { } rawEnum) integral = rawEnum;
                (int bits, bool signed) = integral switch
                {
                    "sbyte" => (8, true),
                    "byte" => (8, false),
                    "short" => (16, true),
                    "ushort" => (16, false),
                    "int" => (32, true),
                    "uint" => (32, false),
                    "long" => (64, true),
                    "ulong" => (64, false),
                    "nint" => (plan.api.interop.pointerSize * 8, true),
                    "nuint" => (plan.api.interop.pointerSize * 8, false),
                    _ => throw new ArgumentException("An integer default requires an exact integral or enumeration friendly type.")
                };
                BigInteger minimum = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
                BigInteger maximum = (BigInteger.One << (signed ? bits - 1 : bits)) - 1;
                if (integer < minimum || integer > maximum) throw new ArgumentException("An integer default is outside its exact friendly type range.");
                string literal = integer.ToString(CultureInfo.InvariantCulture) + (signed ? bits == 64 ? "L" : "" : "UL");
                return "(" + type + ")(" + literal + ")";
            case ManagedDefaultValueKind.FloatingPoint:
                if (scalarType == "float" && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float single) && float.IsFinite(single))
                    return single.ToString("R", CultureInfo.InvariantCulture) + "F";
                if (scalarType == "double" && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
                    return number.ToString("R", CultureInfo.InvariantCulture) + "D";
                throw new ArgumentException("A floating-point default requires a finite value within its exact friendly type.");
            default: throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static string Canonical(string type) => type.Replace("global::", "", StringComparison.Ordinal) switch
    {
        "System.Byte" => "byte",
        "System.SByte" => "sbyte",
        "System.Int16" => "short",
        "System.UInt16" => "ushort",
        "System.Int32" => "int",
        "System.UInt32" => "uint",
        "System.Int64" => "long",
        "System.UInt64" => "ulong",
        "System.IntPtr" => "nint",
        "System.UIntPtr" => "nuint",
        "System.Single" => "float",
        "System.Double" => "double",
        "System.Boolean" => "bool",
        "System.Char" => "char",
        "System.String" => "string",
        "System.String?" => "string?",
        _ => type
    };
}
