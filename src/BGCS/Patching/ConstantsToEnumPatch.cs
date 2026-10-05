using System.Collections.Generic;
using BGCS.Analysis;
using BGCS.Analysis.Constants;
using BGCS.Configuration;
using BGCS.Conversion;
using BGCS.Core.Text;
using BGCS.CppAst.Model;
using BGCS.Metadata;

namespace BGCS.Patching;

/// <summary>
/// Contributes a configured managed enumeration from selected macro values and suppresses their standalone constant projections.
/// </summary>
public class ConstantsToEnumPatch : PrePatch
{
    private readonly string m_macroPrefix;
    private readonly string m_csEnumName;
    private readonly string m_baseType;
    private readonly HashSet<string> m_ignored;
    private readonly HashSet<string> m_extra;
    /// <summary>
    /// Captures macro selection and the generated enumeration presentation.
    /// </summary>
    /// <param name="macroPrefix">
    /// The prefix selecting source macro identifiers.
    /// </param>
    /// <param name="csEnumName">
    /// The managed enum identifier to emit.
    /// </param>
    /// <param name="baseType">
    /// The managed integral base-type expression.
    /// </param>
    /// <param name="ignored">
    /// Exact macro names excluded from selection; null creates an empty retained set.
    /// </param>
    /// <param name="extra">
    /// Exact macro names included regardless of prefix; null creates an empty retained set.
    /// </param>
    public ConstantsToEnumPatch(
        string macroPrefix,
        string csEnumName,
        string baseType,
        HashSet<string>? ignored = null,
        HashSet<string>? extra = null
    ) {
        this.m_macroPrefix = macroPrefix;
        this.m_csEnumName = csEnumName;
        this.m_baseType = baseType;
        this.m_ignored = ignored ?? [];
        this.m_extra = extra ?? [];
    }

    /// <inheritdoc/>
    protected override void PatchCompilation(
        CsCodeGeneratorConfig settings,
        ParseResult result
    ) {
        var compilation = result.compilation;
        List<CppMacro> keyEnums = [];
        HashSet<string> itemNames = [];
        foreach (var macro in compilation.macros)
        {
            if (this.m_ignored.Contains(macro.name))
                continue;
            if (macro.name.StartsWith(this.m_macroPrefix) || this.m_extra.Contains(macro.name))
            {
                keyEnums.Add(macro);
                itemNames.Add(macro.name);
            }
        }

        CsEnumMetadata metadata = new(this.m_macroPrefix, this.m_csEnumName, [], null)
        {
            baseType = this.m_baseType
        };
        var prefix = settings.GetEnumNamePrefixEx(this.m_macroPrefix);
        foreach (var macro in keyEnums)
        {
            var itemName = settings.GetEnumName(macro.name, prefix);
            string csValue = macro.value;
            if (csValue.IsNumeric(out var numberType, NumberParseOptions.All))
            {
                if (numberType == NumberType.AnyFloat)
                {
                    continue;
                }
            }
            else if (csValue.IsConstantExpression())
            {
                continue;
            }
            else if (csValue.IsString())
            {
                continue;
            }
            else if (itemNames.Contains(csValue))
            {
                csValue = settings.GetEnumName(csValue, prefix);
            }
            else
            {
                foreach (var item in itemNames)
                {
                    var index = csValue.IndexOf(item);
                    if (index != -1)
                    {
                        csValue = csValue.Remove(index, item.Length);
                        csValue = csValue.Insert(index, settings.GetEnumName(item, prefix));
                    }
                }
            }

            CsEnumItemMetadata itemMeta = new(macro.name, macro.value, itemName, csValue, [], null);
            metadata.items.Add(itemMeta);
            settings.ignoredConstants.Add(macro.name);
        }

        settings.customEnums.Add(metadata);
    }
}
