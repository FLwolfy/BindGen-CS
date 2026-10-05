namespace BGCS.CppAst.Parsing;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using BGCS.CppAst.Model.Types;

/// <summary>
/// Defines values for <c>ResolverScope</c>.
/// </summary>
[Flags]
internal enum ResolverScope
{
    None = 0,
    System = 1,
    User = 2,
    All = System | User,
}

/// <summary>
/// Defines the public class <c>TypedefResolver</c>.
/// </summary>
internal class TypedefResolver
{
    private readonly Dictionary<CursorKey, CppType> m_typedefs = [];
    /// <summary>
    /// Executes public operation <c>RegisterTypedef</c>.
    /// </summary>
    public void RegisterTypedef(
        CursorKey key,
        CppType type
    ) {
        if (this.m_typedefs.TryGetValue(key, out var cppPreviousCppType))
        {
            Debug.Assert(cppPreviousCppType.GetType() == type.GetType());
        }
        else
        {
            this.m_typedefs.Add(key, type);
        }
    }

    /// <summary>
    /// Executes public operation <c>TryResolve</c>.
    /// </summary>
    public bool TryResolve(
        CursorKey key,
        [NotNullWhen(true)] out CppType? type,
        ResolverScope scope = ResolverScope.All
    ) {
        if ((scope & ResolverScope.User) != 0)
        {
            if (this.m_typedefs.TryGetValue(key.WithScope(ResolverScope.User), out type))
                return true;
        }

        if ((scope & ResolverScope.System) != 0)
        {
            if (this.m_typedefs.TryGetValue(key.WithScope(ResolverScope.System), out type))
                return true;
        }

        type = null;
        return false;
    }
}
