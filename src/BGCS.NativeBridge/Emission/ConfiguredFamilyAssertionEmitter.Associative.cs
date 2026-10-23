using System;
using System.Text;
using BGCS.Core.Bridge;
using BGCS.NativeBridge.Planning;

namespace BGCS.NativeBridge.Emission;

internal static partial class ConfiguredFamilyAssertionEmitter
{
    private static bool EmitAssociativeOrExpected(
        NativeBridgePlan module,
        StandardValueOperation facts,
        string owner,
        string element,
        int index,
        StringBuilder output,
        Action<string, string> check
    ) {
        if (facts.family is not (StandardValueKind.Map or StandardValueKind.Set or StandardValueKind.Expected)) return false;
        string mapped = Name(index, "mapped"), iterator = Name(index, "read"), count = Name(index, "count");
        string Bare(string expression) => "typename std::remove_cv<typename std::remove_reference<decltype(" + expression + ")>::type>::type";
        if (facts.family is StandardValueKind.Map or StandardValueKind.Expected)
            output.Append("using ").Append(mapped).Append(" = ").Append(NativeTypeSpelling.Format(module.source, facts.argumentTypes[1])).AppendLine(";");
        if (facts.family == StandardValueKind.Expected)
        {
            check("std::is_same<typename " + owner + "::value_type, " + element + ">::value", "exact native expected value type");
            check("std::is_same<typename " + owner + "::error_type, " + mapped + ">::value", "exact native expected error type");
            check("std::is_same<decltype(std::declval<const " + owner + "&>().has_value()), bool>::value", "expected presence result");
            check("std::is_same<" + Bare("std::declval<const " + owner + "&>().value()") + ", " + element + ">::value", "exact expected value access");
            check("std::is_same<" + Bare("std::declval<const " + owner + "&>().error()") + ", " + mapped + ">::value", "exact expected error access");
            return true;
        }
        check("std::is_same<typename " + owner + "::" + (facts.family == StandardValueKind.Map ? "key_type" : "value_type") + ", " + element + ">::value", "exact native element type");
        if (facts.family == StandardValueKind.Map)
            check("std::is_same<typename " + owner + "::mapped_type, " + mapped + ">::value", "exact native mapped type");
        output.Append("using ").Append(count).Append(" = ").Append(Bare("std::declval<const " + owner + "&>().size()")).AppendLine(";")
            .Append("using ").Append(iterator).Append(" = decltype(std::declval<const ").Append(owner).AppendLine("&>().begin());")
            .Append("using ").Append(Name(index, "advance")).Append(" = decltype(++std::declval<").Append(iterator).AppendLine("&>());");
        check("std::is_integral<" + count + ">::value && std::is_unsigned<" + count + ">::value && !std::is_same<" + count + ", bool>::value && sizeof(" + count + ") <= sizeof(uint64_t)", "bounded unsigned native count");
        check("std::is_same<decltype(std::declval<const " + owner + "&>().end()), " + iterator + ">::value", "matching native iterator sentinel");
        check("std::is_same<decltype(std::declval<const " + iterator + "&>() == std::declval<const " + iterator + "&>()), bool>::value", "native iterator equality result");
        if (facts.family == StandardValueKind.Map)
        {
            check("std::is_same<" + Bare("std::declval<" + iterator + "&>()->first") + ", " + element + ">::value", "exact native iterator key");
            check("std::is_same<" + Bare("std::declval<" + iterator + "&>()->second") + ", " + mapped + ">::value", "exact native iterator mapped value");
            output.Append("using ").Append(Name(index, "write")).Append(" = decltype(std::declval<").Append(owner)
                .Append("&>().insert_or_assign(std::declval<").Append(element).Append(">(), std::declval<").Append(mapped).AppendLine(">()));");
        }
        else
        {
            check("std::is_same<" + Bare("*std::declval<" + iterator + "&>()") + ", " + element + ">::value", "exact native set iterator value");
            output.Append("using ").Append(Name(index, "write")).Append(" = decltype(std::declval<").Append(owner)
                .Append("&>().insert(std::declval<").Append(element).AppendLine(">()));");
        }
        return true;
    }
}
