using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;

namespace BGCS.CppAst.Extensions;

/// <summary>
/// Queries class dispatch requirements from the parsed inheritance graph.
/// </summary>
public static class CppClassExtensions
{
    /// <summary>
    /// Determines whether the class or any parsed base class declares virtual members.
    /// </summary>
    /// <param name = "classNode">
    /// The class whose constructors, destructor, methods, and base classes are inspected.
    /// </param>
    /// <returns>
    /// True when virtual dispatch is present; false when no parsed member requires it.
    /// </returns>
    public static bool HasVirtualMembers(this CppClass classNode)
    {
        foreach (CppFunction destructor in classNode.destructors)
            if (destructor.isVirtual)
                return true;
        foreach (CppFunction constructor in classNode.constructors)
            if (constructor.isVirtual)
                return true;
        foreach (CppFunction function in classNode.functions)
            if (function.isVirtual)
                return true;
        foreach (CppBaseType baseType in classNode.baseTypes)
            if (baseType.type is CppClass baseClass && baseClass.HasVirtualMembers())
                return true;
        return false;
    }
}
