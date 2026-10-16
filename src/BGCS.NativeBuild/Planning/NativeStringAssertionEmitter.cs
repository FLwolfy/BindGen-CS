using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BGCS.Contracts.Native;
using BGCS.NativeBuild.Validation;

namespace BGCS.NativeBuild.Planning;

/// <summary>Checks actual native-preprocessed literal tokens using the selected compiler's execution-character conversion.</summary>
public sealed class NativeStringAssertionEmitter
{
    /// <summary>Creates a headerless C++ constexpr probe without reconstructing literals from expected bytes.</summary>
    /// <param name="expected">Selected source constants used by generated managed declarations.</param>
    /// <param name="actual">The independently parsed actual native-preprocessed source, including exact expanded literal tokens.</param>
    /// <returns>A C++ verification input, or null when no selected string constant exists.</returns>
    /// <remarks>The original C headers are never reparsed under C++ conditions. Every expected code unit, including embedded and final NUL, is checked independently.</remarks>
    public string? Emit(
        NativeModule expected,
        NativeModule actual
    ) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        NativeDeclaration[] constants = SelectConstants(expected);
        if (constants.Length == 0) return null;
        StringBuilder source = new("/* BGCS actual native execution-character verification; original headers deliberately absent. */\n");
        foreach (string keyword in new[] { "static_assert", "constexpr", "auto", "sizeof", "unsigned", "long", "static_cast" })
            source.Append("#if defined(").Append(keyword).AppendLine(")\n#error BGCS native string proof token is shadowed by a macro\n#endif");
        for (int index = 0; index < constants.Length; index++)
        {
            NativeDeclaration declaration = constants[index];
            NativeStringConstant value = declaration.stringConstant!;
            NativeStringConstant effective = NativeSourceFactComparer.EffectiveConstant(declaration, actual)?.stringConstant
                ?? throw new InvalidOperationException("Actual native preprocessing did not retain a selected string constant.");
            if (effective.literalTokens.IsDefaultOrEmpty)
                throw new NotSupportedException("Actual native string code units require exact expanded literal tokens; synthesizing an expected-byte literal cannot prove execution encoding.");
            if (value.codeUnits.IsDefaultOrEmpty || value.codeUnits[^1] != 0 || value.unitWidth is not (1 or 2 or 4)
                || !Enum.IsDefined(value.kind) || value.kind is NativeStringLiteralKind.Ordinary or NativeStringLiteralKind.Utf8 && value.unitWidth != 1
                || value.kind == NativeStringLiteralKind.Utf16 && value.unitWidth != 2 || value.kind == NativeStringLiteralKind.Utf32 && value.unitWidth != 4)
                throw new InvalidOperationException("A selected string constant has no supported complete code-unit contract.");
            ulong mask = value.unitWidth switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, _ => uint.MaxValue };
            if (value.codeUnits.Any(unit => unit > mask)) throw new InvalidOperationException("A selected native string code unit exceeds its declared width.");
            string name = "bgcs_literal_" + index.ToString(CultureInfo.InvariantCulture);
            source.Append("#if defined(").Append(name).AppendLine(")\n#error BGCS native string proof identifier is shadowed by a macro\n#endif");
            source.Append("constexpr auto& ").Append(name).Append(" = (").Append(string.Join(' ', effective.literalTokens)).AppendLine(");");
            Check("sizeof(" + name + "[0])", value.unitWidth, "code-unit width");
            Check("sizeof(" + name + ") / sizeof(" + name + "[0])", value.codeUnits.Length, "complete literal length");
            for (int unit = 0; unit < value.codeUnits.Length; unit++)
                Check("(static_cast<unsigned long long>(" + name + "[" + unit.ToString(CultureInfo.InvariantCulture) + "]) & " + mask.ToString(CultureInfo.InvariantCulture) + "ULL)",
                    value.codeUnits[unit], "exact code-unit value");
        }
        return source.ToString();

        void Check(
            string expression,
            long value,
            string fact
        ) => source.Append("static_assert((").Append(expression).Append(") == ")
            .Append(value.ToString(CultureInfo.InvariantCulture)).Append("ULL, \"BGCS native execution-character ").Append(fact).AppendLine(" differs from generated constants\");");
    }

    internal static NativeDeclaration[] SelectConstants(NativeModule module) => module.declarations.Where(declaration => !declaration.isSystemDeclaration
        && declaration.stringConstant is not null && declaration.visibility is not ("Private" or "Protected")
        && (declaration.kind == "Macro" && declaration.macro is { isFunctionLike: false, isActive: true }
            || declaration.kind == "Field" && declaration.role == "Variable")).ToArray();
}
