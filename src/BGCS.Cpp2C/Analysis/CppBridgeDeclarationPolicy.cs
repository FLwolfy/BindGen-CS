using System.Collections.Generic;
using System.Linq;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;

namespace BGCS.Cpp2C.Analysis;

/// <summary>
/// Selects declarations that a generated bridge can legally name outside their C++ owner.
/// </summary>
internal static class CppBridgeDeclarationPolicy
{
    internal static bool IsAccessible(CppElement declaration)
    {
        CppElement? current = declaration;
        while (current is not null)
        {
            if (current is ICppMemberWithVisibility { visibility: CppVisibility.Private or CppVisibility.Protected })
                return false;
            current = current.parent as CppElement;
        }
        return true;
    }

    internal static IEnumerable<CppClass> EnumerateAccessibleClasses(IEnumerable<CppClass> classes)
    {
        foreach (CppClass cppClass in classes)
        {
            if (!IsAccessible(cppClass))
                continue;
            yield return cppClass;
            foreach (CppClass nested in EnumerateAccessibleClasses(cppClass.classes))
                yield return nested;
        }
    }

    internal static bool RequiresOpaqueBridge(CppClass cppClass) =>
        cppClass.classKind == CppClassKind.Class
        || cppClass.baseTypes.Count > 0
        || cppClass.functions.Count > 0
        || cppClass.specializedTemplate?.functions.Count > 0
        || cppClass.constructors.Count > 0
        || cppClass.specializedTemplate?.constructors.Count > 0
        || cppClass.destructors.Count > 0
        || cppClass.fields.Any(static field => !IsAccessible(field))
        || cppClass.HasVirtualMembers();
}
