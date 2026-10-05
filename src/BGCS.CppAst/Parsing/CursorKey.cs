using System;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

namespace BGCS.CppAst.Parsing;

/// <summary>
/// Identifies a declaration within one translation unit using its complete decoded Clang name.
/// </summary>
internal readonly struct CursorKey : IEquatable<CursorKey>
{
    private readonly string m_name;
    private readonly bool m_isAnonymous;
    private readonly uint m_anonymousHash;

    internal CursorKey(
        CppContainerContext context,
        CXCursor cursor
    ) {
        scope = context.type == CppContainerContextType.User ? ResolverScope.User : ResolverScope.System;
        while (cursor.Kind == CXCursorKind.CXCursor_LinkageSpec)
            cursor = cursor.SemanticParent;

        this.cursor = cursor;
        string name = CXUtil.GetCursorUsrString(cursor);
        m_name = name.Length == 0 ? CXUtil.GetCursorDisplayName(cursor) : name;
        m_isAnonymous = cursor.IsAnonymous;
        m_anonymousHash = m_isAnonymous ? cursor.Hash : 0;
    }

    internal ResolverScope scope { get; }
    internal CXCursor cursor { get; }

    private CursorKey(
        CursorKey key,
        ResolverScope scope
    ) {
        this.scope = scope;
        cursor = key.cursor;
        m_name = key.m_name;
        m_isAnonymous = key.m_isAnonymous;
        m_anonymousHash = key.m_anonymousHash;
    }

    internal CursorKey WithScope(ResolverScope scope) => new(this, scope);

    public override bool Equals(object? obj) => obj is CursorKey other && Equals(other);

    public bool Equals(CursorKey other)
    {
        return scope == other.scope
            && m_isAnonymous == other.m_isAnonymous
            && m_anonymousHash == other.m_anonymousHash
            && StringComparer.Ordinal.Equals(m_name, other.m_name);
    }

    public override int GetHashCode() => HashCode.Combine(scope, m_name, m_isAnonymous, m_anonymousHash);

    public static bool operator ==(
        CursorKey left,
        CursorKey right
    ) => left.Equals(right);

    public static bool operator !=(
        CursorKey left,
        CursorKey right
    ) => !left.Equals(right);
}
