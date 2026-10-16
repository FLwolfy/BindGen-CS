using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Contracts.Native;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;

namespace BGCS.NativeBuild.Planning;

/// <summary>Emits original source inputs for native preprocessing without injecting generator assertion-library headers.</summary>
public sealed class NativeFactInputEmitter
{
    /// <summary>Preserves the original header order, preamble and postamble for independent effective-source verification.</summary>
    /// <param name="headers">The exact original absolute header inputs.</param>
    /// <param name="preamble">Explicit source input placed before the headers.</param>
    /// <param name="postamble">Explicit source input placed after the headers.</param>
    /// <param name="module">Selected scalar, string and exact function-alias macro facts to materialize after native preprocessing.</param>
    /// <returns>A preprocessing-only source input; it must not become a second compiled translation unit.</returns>
    public string Emit(
        ImmutableArray<string> headers,
        string preamble = "",
        string postamble = "",
        NativeModule? module = null
    ) {
        if (headers.IsDefaultOrEmpty) throw new ArgumentException("Native source fact verification requires original header inputs.", nameof(headers));
        StringBuilder source = new("/* BGCS original source input for native fact verification; preprocessing only. */\n");
        source.Append(preamble).Append('\n');
        foreach (string header in headers)
        {
            if (header.Any(character => character is '\r' or '\n' or '"' or '\0'))
                throw new ArgumentException("A native fact header cannot be represented as a quoted include.", nameof(headers));
            source.Append("#include \"").Append(header.Replace('\\', '/')).AppendLine("\"");
        }
        source.Append(postamble).Append('\n');
        if (module is not null)
            foreach (NativeDeclaration macro in module.declarations.Where(declaration => !declaration.isSystemDeclaration && declaration.kind == "Macro"
                && (declaration.constantValue is not null || declaration.stringConstant is not null || declaration.macro?.functionAlias is not null)
                && declaration.macro is { isFunctionLike: false, isActive: true }))
            {
                if (!Regex.IsMatch(macro.name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                    throw new ArgumentException("A selected macro fact requires a controlled native identifier.", nameof(module));
                source.Append("\n#if !defined(").Append(macro.name).AppendLine(")");
                source.AppendLine("#error BGCS selected macro is no longer defined under native compilation inputs");
                source.AppendLine("#endif");
                // The native compiler only preprocesses this input. Type deduction is
                // performed by the independent fact frontend after the native -D and
                // header branches have expanded the exact expression.
                source.Append(module.language == SourceLanguage.Cpp ? "const auto " : "const __auto_type ")
                    .Append(MacroProbeName(macro.id)).Append(" = (").Append(macro.name).AppendLine(");");
            }
        return source.ToString();
    }

    internal static string MacroProbeName(StableId identity) => "bgcs_native_scalar_fact_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity.value)));
}
