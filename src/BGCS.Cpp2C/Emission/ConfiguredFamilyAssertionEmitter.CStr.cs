using System;
using System.Text;
using BGCS.Analysis.Bridge;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Cpp2C.Planning;

namespace BGCS.Cpp2C.Emission;

internal static partial class ConfiguredFamilyAssertionEmitter
{
    private static bool EmitCStr(
        CppBridgeModule module,
        StandardLibraryFactReader reader,
        StableId ownerId,
        string owner,
        string element,
        StringBuilder output,
        Action<string, string> check
    ) {
        CppFamilyBinding? binding = reader.FindBinding(module.source, ownerId);
        if (binding?.stringAccess != CppStringAccess.CStrNullTerminated) return false;
        CppCStrFact facts = reader.ReadCStr(module.source, ownerId);
        string pointer = NativeTypeSpelling.Format(module.source, module.source.GetDeclaration(facts.member).returnType!.Value);
        check("std::is_same<decltype(std::declval<" + (facts.isConst ? "const " : "") + owner + "&>().c_str()), " + pointer + ">::value", "exact native c_str pointer result");
        check("std::is_integral<" + element + ">::value && sizeof(" + element + ") == 1 && !std::is_same<" + element + ", bool>::value", "one-byte native c_str code units");
        if (binding.cStr!.constructorCopiesInput is not null)
            check("std::is_constructible<" + owner + ", const " + element + "*>::value", "native copying pointer constructor signature");
        return true;
    }
}
