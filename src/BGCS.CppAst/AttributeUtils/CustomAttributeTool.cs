using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BGCS.CppAst.AttributeUtils;
/// <summary>
/// Defines the public class <c>MetaAttribute</c>.
/// </summary>
public class MetaAttribute
{
    /// <summary>
    /// Exposes public member <c>FeatureName</c>.
    /// </summary>
    public string FeatureName;
    /// <summary>
    /// Exposes public member <c>[]</c>.
    /// </summary>
    public Dictionary<string, object> ArgumentMap = [];

    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append($"{FeatureName} {{");
        foreach (var kvp in ArgumentMap)
        {
            builder.Append($"{kvp.Key}: {kvp.Value}, ");
        }
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// Executes public operation <c>QueryKeyIsTrue</c>.
    /// </summary>
    public bool QueryKeyIsTrue(string key)
    {
        return ArgumentMap.ContainsKey(key) && (ArgumentMap[key] is bool && (bool)ArgumentMap[key] || ArgumentMap[key] is string && (string)ArgumentMap[key] == "true");
    }

    /// <summary>
    /// Executes public operation <c>QueryKeysAreTrue</c>.
    /// </summary>
    public bool QueryKeysAreTrue(List<string> keys)
    {
        if (keys == null || !keys.Any())
        {
            return false;
        }

        foreach (string key in keys)
        {
            if (!QueryKeyIsTrue(key))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Defines the public class <c>MetaAttributeMap</c>.
/// </summary>
public class MetaAttributeMap
{
    private readonly List<MetaAttribute> list = [];

    /// <summary>
    /// Exposes public member <c>IsNull</c>.
    /// </summary>
    public bool IsNull
    {
        get
        {
            return list.Count == 0;
        }
    }

    /// <summary>
    /// Executes public operation <c>QueryArgument</c>.
    /// </summary>
    public object QueryArgument(string argName)
    {
        if (list.Count == 0) return null;

        foreach (var argMap in list)
        {
            if (argMap.ArgumentMap.ContainsKey(argName))
            {
                return argMap.ArgumentMap[argName];
            }
        }

        return null;
    }

    /// <summary>
    /// Executes public operation <c>QueryArgumentAsBool</c>.
    /// </summary>
    public bool QueryArgumentAsBool(string argName, bool defaultVal)
    {
        var obj = QueryArgument(argName);
        if (obj != null)
        {
            try
            {
                return Convert.ToBoolean(obj);
            }
            catch (Exception)
            {
            }
        }

        return defaultVal;
    }

    /// <summary>
    /// Executes public operation <c>QueryArgumentAsInteger</c>.
    /// </summary>
    public int QueryArgumentAsInteger(string argName, int defaultVal)
    {
        var obj = QueryArgument(argName);
        if (obj != null)
        {
            try
            {
                return Convert.ToInt32(obj);
            }
            catch (Exception)
            {
            }
        }

        return defaultVal;
    }

    /// <summary>
    /// Executes public operation <c>QueryArgumentAsString</c>.
    /// </summary>
    public string QueryArgumentAsString(string argName, string defaultVal)
    {
        var obj = QueryArgument(argName);
        if (obj != null)
        {
            try
            {
                return Convert.ToString(obj);
            }
            catch (Exception)
            {
            }
        }

        return defaultVal;
    }

    /// <summary>
    /// Executes public operation <c>Append</c>.
    /// </summary>
    public void Append(MetaAttribute? metaAttr)
    {
        if (metaAttr is null)
        {
            return;
        }

        foreach (MetaAttribute meta in list)
        {
            foreach (KeyValuePair<string, object> kvp in meta.ArgumentMap)
            {
                metaAttr.ArgumentMap.Remove(kvp.Key);
            }
        }

        if (metaAttr.ArgumentMap.Count > 0)
        {
            list.Add(metaAttr);
        }
    }
}

/// <summary>
/// Defines the public class <c>CustomAttributeTool</c>.
/// </summary>
public static class CustomAttributeTool
{
    /// <summary>
    /// Exposes public member <c>"rmeta"</c>.
    /// </summary>
    public const string kMetaLeaderWord = "rmeta";
    /// <summary>
    /// Exposes public member <c>"class"</c>.
    /// </summary>
    public const string kMetaClassLeaderWord = "class";
    /// <summary>
    /// Exposes public member <c>"function"</c>.
    /// </summary>
    public const string kMetaFunctionLeaderWord = "function";
    /// <summary>
    /// Exposes public member <c>"field"</c>.
    /// </summary>
    public const string kMetaFieldLeaderWord = "field";
    /// <summary>
    /// Defines a public enumeration.
    /// </summary>
    public const string kMetaEnumLeaderWord = "enum";
    const string kMetaNotSetWord = "not_set_internal";
    const string kMetaSeparate = "____";
    const string kMetaArgumentSeparate = "|";
    const string kMetaStartWord = kMetaLeaderWord + kMetaSeparate;

    /// <summary>
    /// Executes public operation <c>IsRstudioAttribute</c>.
    /// </summary>
    public static bool IsRstudioAttribute(string meta)
    {
        return meta.StartsWith(kMetaStartWord);
    }

    private static List<string> DivideForMetaAttribute(string meta)
    {
        var attrArray = meta.Split(kMetaSeparate);
        var retList = new List<string>();

        for (int i = 1; i < attrArray.Length; i++)
        {
            retList.Add(attrArray[i]);
        }

        return retList;
    }

    /// <summary>
    /// Executes public operation <c>ParseMetaStringFor</c>.
    /// </summary>
    public static MetaAttribute ParseMetaStringFor(string meta, string needLeaderWord, out string errorMessage)
    {
        string feature = "", arguments = "";
        errorMessage = "";

        if (!IsRstudioAttribute(meta))
        {
            return null;
        }

        List<string> tmpList = DivideForMetaAttribute(meta);
        if (tmpList.Count < 2 || tmpList[0] != needLeaderWord)
        {
            return null;
        }

        var arrVal = tmpList[1].Split(kMetaArgumentSeparate);
        feature = arrVal[0];
        if (arrVal.Length >= 2)
        {
            arguments = arrVal[1];
        }

        MetaAttribute attribute = new();
        attribute.FeatureName = feature;
        bool parseSuc = NamedParameterParser.ParseNamedParameters(arguments, attribute.ArgumentMap, out errorMessage);
        if (parseSuc)
        {
            return attribute;
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// Executes public operation <c>ParseMetaStringFor</c>.
    /// </summary>
    public static MetaAttribute ParseMetaStringFor(string meta, out string errorMessage)
    {
        errorMessage = "";
        MetaAttribute attribute = new();
        bool parseSuc = NamedParameterParser.ParseNamedParameters(meta, attribute.ArgumentMap, out errorMessage);
        if (parseSuc)
        {
            return attribute;
        }

        return null;
    }
}
