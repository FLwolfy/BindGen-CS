using System;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Interfaces;

namespace BGCS.CppAst.Parsing;
/// <summary>
/// Defines the public class <c>CppContainerContext</c>.
/// </summary>
public class CppContainerContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppContainerContext"/>.
    /// </summary>
    public CppContainerContext(ICppContainer container, CppContainerContextType type, CppVisibility visibility = CppVisibility.Default)
    {
        Container = container;
        Type = type;
        CurrentVisibility = visibility;
    }

    /// <summary>
    /// Executes public operation <c>CppContainerContext</c>.
    /// </summary>
    public CppContainerContext(ICppContainer container, CppVisibility visibility = CppVisibility.Default)
    {
        Container = container;
        Type = CppContainerContextType.Unspecified;
        CurrentVisibility = visibility;
    }

    /// <summary>
    /// Gets <c>Container</c>.
    /// </summary>
    public ICppContainer Container { get; }

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public ICppDeclarationContainer DeclarationContainer => (ICppDeclarationContainer)Container;

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public ICppGlobalDeclarationContainer GlobalDeclarationContainer => (ICppGlobalDeclarationContainer)Container;

    /// <summary>
    /// Gets or sets <c>CurrentVisibility</c>.
    /// </summary>
    public CppVisibility CurrentVisibility { get; set; }

    /// <summary>
    /// Gets <c>Type</c>.
    /// </summary>
    public CppContainerContextType Type { get; }

    /// <summary>
    /// Gets or sets <c>IsChildrenVisited</c>.
    /// </summary>
    public bool IsChildrenVisited { get; set; }
}
