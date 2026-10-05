using System;
using System.Collections.Generic;
using ClangSharp.Interop;

namespace BGCS.CppAst.Parsing;

/// <summary>
/// Owns parser visitors for one model context; no live visitor is shared across compilations.
/// </summary>
internal sealed class CursorVisitorRegistry<TVisitor, TResult>
    where TVisitor : CursorVisitor<TResult>
{
    private readonly Dictionary<CXCursorKind, TVisitor> m_visitors = [];
    private readonly Dictionary<Type, TVisitor> m_types = [];

    internal void Register<T>() where T : TVisitor, new()
    {
        T visitor = new();
        m_types.Add(typeof(T), visitor);
        foreach (CXCursorKind kind in visitor.kinds)
            m_visitors.Add(kind, visitor);
    }

    internal TVisitor? GetVisitor(CXCursorKind kind) => m_visitors.GetValueOrDefault(kind);

    internal T GetVisitor<T>() where T : TVisitor => (T)m_types[typeof(T)];
}
