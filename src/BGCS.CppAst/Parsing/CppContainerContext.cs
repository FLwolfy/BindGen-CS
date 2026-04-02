using System;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Interfaces;

namespace BGCS.CppAst.Parsing;
public class CppContainerContext
{
    public CppContainerContext(ICppContainer container, CppContainerContextType type, CppVisibility visibility = CppVisibility.Default)
    {
        Container = container;
        Type = type;
        CurrentVisibility = visibility;
    }

    public CppContainerContext(ICppContainer container, CppVisibility visibility = CppVisibility.Default)
    {
        Container = container;
        Type = CppContainerContextType.Unspecified;
        CurrentVisibility = visibility;
    }

    public ICppContainer Container { get; }

    public ICppDeclarationContainer DeclarationContainer => (ICppDeclarationContainer)Container;

    public ICppGlobalDeclarationContainer GlobalDeclarationContainer => (ICppGlobalDeclarationContainer)Container;

    public CppVisibility CurrentVisibility { get; set; }

    public CppContainerContextType Type { get; }

    public bool IsChildrenVisited { get; set; }
}
