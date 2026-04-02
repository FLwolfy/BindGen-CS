using System;
using System.Collections.Generic;
using ClangSharp.Interop;

namespace BGCS.CppAst.Parsing;
public class CursorVisitorRegistry<TVisitor, TResult> where TVisitor : CursorVisitor<TResult>
{
    private readonly Dictionary<CXCursorKind, TVisitor> visitors = [];
    private readonly Dictionary<Type, TVisitor> visitorTypes = [];

    public T Register<T>() where T : TVisitor, new()
    {
        var t = new T();
        Register(t);
        return t;
    }

    public void Register<T>(T visitor) where T : TVisitor
    {
        visitorTypes.Add(typeof(T), visitor);
        foreach (var kind in visitor.Kinds)
        {
            visitors[kind] = visitor;
        }
    }

    public void Override<T>(TVisitor visitor) where T : TVisitor
    {
        var old = GetVisitor<T>();
        foreach (var kind in old.Kinds)
        {
            visitors.Remove(kind);
        }
        visitorTypes[typeof(T)] = visitor;
        foreach (var kind in visitor.Kinds)
        {
            visitors[kind] = visitor;
        }
    }

    public void Unregister<T>() where T : TVisitor
    {
        var old = GetVisitor<T>();
        foreach (var kind in old.Kinds)
        {
            visitors.Remove(kind);
        }
        visitorTypes.Remove(typeof(T));
    }

    public T GetVisitor<T>() where T : TVisitor
    {
        return (T)visitorTypes[typeof(T)];
    }

    public TVisitor? GetVisitorByKind(CXCursorKind kind)
    {
        visitors.TryGetValue(kind, out var visitor);
        return visitor;
    }
}
