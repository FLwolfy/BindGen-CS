using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class DocumentationProjector
{
    // Native prose is data. XML tags, Doxygen commands and source-line delimiters
    // must never become C# declarations, XML includes or parameter-name assertions.
    internal static void Append(
        StringBuilder text,
        string? documentation,
        string indent = ""
    )
        => AppendSection(text, "summary", documentation, indent);

    internal static void AppendMethod(
        StringBuilder text,
        string? fallback,
        CSharpCallableDocumentationPlan? documentation,
        IReadOnlyList<string> parameterNames,
        IEnumerable<int> selected,
        string returnType,
        bool raw,
        string indent
    ) {
        if (documentation is null) { Append(text, fallback, indent); return; }
        Append(text, documentation.summary, indent);
        foreach (int index in selected)
            AppendSection(text, "param name=\"" + parameterNames[index].TrimStart('@') + "\"", documentation.parameters[index] ?? "Native parameter.", indent);
        List<string> notes = documentation.notes.ToList();
        if (!string.IsNullOrWhiteSpace(documentation.returns))
        {
            if (returnType == "void" || raw && documentation.statusReturn) notes.Add("Native result: " + documentation.returns);
            else AppendSection(text, "returns", documentation.returns, indent);
        }
        if (raw && documentation.statusReturn && returnType != "void") AppendSection(text, "returns", "The native status code.", indent);
        if (notes.Count > 0) AppendSection(text, "remarks", string.Join(Environment.NewLine, notes), indent);
    }

    private static void AppendSection(
        StringBuilder text,
        string tag,
        string? documentation,
        string indent
    ) {
        if (string.IsNullOrWhiteSpace(documentation)) return;
        text.Append(indent).Append("/// <").Append(tag).AppendLine(">");
        foreach (string line in documentation.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            text.Append(indent).Append("/// ");
            foreach (Rune character in line.EnumerateRunes())
            {
                int value = character.Value;
                if (value == '&') text.Append("&amp;");
                else if (value == '<') text.Append("&lt;");
                else if (value == '>') text.Append("&gt;");
                else if (value == '"') text.Append("&quot;");
                else if (value == '\'') text.Append("&apos;");
                else if (value is 0x85 or 0x2028 or 0x2029)
                    text.Append("&#x").Append(value.ToString("X", CultureInfo.InvariantCulture)).Append(';');
                else if (value == 9 || value is >= 0x20 and <= 0xD7FF or >= 0xE000 and <= 0xFFFD or >= 0x10000 and <= 0x10FFFF)
                    text.Append(character.ToString());
                else text.Append('\uFFFD');
            }
            text.AppendLine();
        }
        string closing = tag.Split(' ')[0];
        text.Append(indent).Append("/// </").Append(closing).AppendLine(">");
    }
}
