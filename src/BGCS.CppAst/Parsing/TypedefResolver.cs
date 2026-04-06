namespace BGCS.CppAst.Parsing;
using BGCS.CppAst.Model.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

[Flags]
/// <summary>
/// Defines values for <c>ResolverScope</c>.
/// </summary>
public enum ResolverScope
{
    None = 0,
    System = 1,
    User = 2,
    All = System | User,
}

/// <summary>
/// Defines the public class <c>TypedefResolver</c>.
/// </summary>
public class TypedefResolver
{
    private readonly Dictionary<CursorKey, CppType> typedefs = [];

    /// <summary>
    /// Executes public operation <c>RegisterTypedef</c>.
    /// </summary>
    public void RegisterTypedef(CursorKey key, CppType type)
    {
        if (typedefs.TryGetValue(key, out var cppPreviousCppType))
        {
            Debug.Assert(cppPreviousCppType.GetType() == type.GetType());
        }
        else
        {
            typedefs.Add(key, type);
        }
    }

    /// <summary>
    /// Executes public operation <c>TryResolve</c>.
    /// </summary>
    public bool TryResolve(CursorKey key, [NotNullWhen(true)] out CppType? type, ResolverScope scope = ResolverScope.All)
    {
        if ((scope & ResolverScope.User) != 0)
        {
            key.scope = ResolverScope.User;
            if (typedefs.TryGetValue(key, out type)) return true;
        }
        if ((scope & ResolverScope.System) != 0)
        {
            key.scope = ResolverScope.System;
            if (typedefs.TryGetValue(key, out type)) return true;
        }
        type = null;
        return false;
    }
}
