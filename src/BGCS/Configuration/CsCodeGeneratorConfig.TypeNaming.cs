using System.Collections.Generic;
using BGCS.Conversion;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.Metadata;
using Newtonsoft.Json;

namespace BGCS.Configuration;

/// <summary>
/// Transforms an attempt-local enum-item projection after native analysis.
/// </summary>
/// <param name="cppEnum">
/// The borrowed native enumeration.
/// </param>
/// <param name="cppEnumItem">
/// The borrowed native item being projected.
/// </param>
/// <param name="csEnum">
/// The mutable containing managed enum projection.
/// </param>
/// <param name="csEnumItem">
/// The mutable managed item projection to transform.
/// </param>
public delegate void CustomEnumItemMapperDelegate(
    CppEnum cppEnum,
    CppEnumItem cppEnumItem,
    CsEnumMetadata csEnum,
    CsEnumItemMetadata csEnumItem
);
/// <summary>
/// Provides nested record naming and an optional attempt-local enum-item mapping callback.
/// </summary>
public partial class CsCodeGeneratorConfig
{
    /// <summary>
    /// Gets or sets the enum-item projection callback; null leaves configured mapping behavior unchanged.
    /// </summary>
    [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
    public CustomEnumItemMapperDelegate? customEnumItemMapper { get; set; }

    [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
    internal Dictionary<string, CsEnumMetadata> definedCppEnums { get; set; } = []; // set to empty just to make sure, because if enum generation is disabled, this will not be set

    /// <summary>
    /// Finds the containing field whose canonical record type matches an anonymous nested record.
    /// </summary>
    /// <param name="parent">Record containing the candidate field.</param>
    /// <param name="union">Nested record sought by canonical type identity.</param>
    /// <returns>The zero-based field position, or -1 when no matching field exists.</returns>
    public static int IndexOfAnonymousField(
        CppClass parent,
        CppClass union
    ) {
        for (int i = 0; i < parent.fields.Count; i++)
        {
            var field = parent.fields[i];
            if (field.type.GetCanonicalRoot(true) == union)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Resolves a nested managed record name using explicit mappings and containing field semantics.
    /// </summary>
    /// <param name="parentClass">Native containing record.</param>
    /// <param name="parentCsName">Resolved managed name of the containing record.</param>
    /// <param name="subClass">Nested native record to name.</param>
    /// <param name="idxSubClass">Nested declaration position used to disambiguate anonymous records.</param>
    /// <returns>The managed nested type identifier selected by the current naming policy.</returns>
    public string GetCsSubTypeName(
        CppClass parentClass,
        string parentCsName,
        CppClass subClass,
        int idxSubClass
    ) {
        string csSubName;
        if (subClass.isAnonymous)
        {
            idxSubClass = IndexOfAnonymousField(parentClass, subClass);
            if (idxSubClass != -1)
            {
                var field = parentClass.fields[idxSubClass];
                if (field.type == subClass)
                {
                    string csFieldName = GetFieldName(field.name);
                    if (string.IsNullOrEmpty(csFieldName))
                    {
                        string label = parentClass.classes.Count == 1 ? "" : idxSubClass.ToString();
                        csSubName = parentCsName + "Anonymous" + label;
                        return csSubName;
                    }

                    csSubName = csFieldName + "Anonymous";
                }
                else if (field.type is CppArrayType)
                {
                    string csFieldName = GetFieldName(field.name);
                    if (csFieldName.EndsWith('s'))
                    {
                        csFieldName = csFieldName[..^1];
                    }

                    csSubName = csFieldName;
                }
                else
                {
                    string label = parentClass.classes.Count == 1 ? "" : idxSubClass.ToString();
                    csSubName = parentCsName + "Anonymous" + label;
                }
            }
            else
            {
                string label = parentClass.classes.Count == 1 ? "" : idxSubClass.ToString();
                csSubName = parentCsName + "Anonymous" + label;
            }
        }
        else
        {
            csSubName = GetCsCleanName(subClass.name);
        }

        foreach (var field in parentClass.fields)
        {
            if (field.name == csSubName)
            {
                csSubName = parentCsName + csSubName;
                break;
            }
        }

        return csSubName;
    }
}
