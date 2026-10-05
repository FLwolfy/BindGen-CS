using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Interfaces;

namespace BGCS.CppAst.Parsing;

/// <summary>
/// Defines the public class <c>CppContainerContext</c>.
/// </summary>
internal class CppContainerContext
{
    /// <summary>
    /// Initializes a new instance of <see cref = "CppContainerContext"/>.
    /// </summary>
    public CppContainerContext(
        ICppContainer container,
        CppContainerContextType type,
        CppVisibility visibility = CppVisibility.Default
    ) {
        this.container = container;
        this.type = type;
        this.currentVisibility = visibility;
    }

    /// <summary>
    /// Executes public operation <c>CppContainerContext</c>.
    /// </summary>
    public CppContainerContext(
        ICppContainer container,
        CppVisibility visibility = CppVisibility.Default
    ) {
        this.container = container;
        this.type = CppContainerContextType.Unspecified;
        this.currentVisibility = visibility;
    }

    /// <summary>
    /// Gets <c>Container</c>.
    /// </summary>
    public ICppContainer container { get; }
    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public ICppDeclarationContainer declarationContainer => (ICppDeclarationContainer)this.container;
    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public ICppGlobalDeclarationContainer globalDeclarationContainer => (ICppGlobalDeclarationContainer)this.container;
    /// <summary>
    /// Gets or sets <c>CurrentVisibility</c>.
    /// </summary>
    public CppVisibility currentVisibility { get; set; }
    /// <summary>
    /// Gets <c>Type</c>.
    /// </summary>
    public CppContainerContextType type { get; }
    /// <summary>
    /// Gets or sets <c>IsChildrenVisited</c>.
    /// </summary>
    public bool isChildrenVisited { get; set; }
}
