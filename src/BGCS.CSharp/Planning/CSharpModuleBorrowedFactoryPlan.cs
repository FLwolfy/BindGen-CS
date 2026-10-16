namespace BGCS.CSharp.Planning;

/// <summary>Plans an explicitly module-valid native pointer without taking native destruction responsibility.</summary>
/// <param name="methodIndex">The unchanged raw import index.</param>
public sealed record CSharpModuleBorrowedFactoryPlan(int methodIndex)
{
    /// <summary>Gets an optional typed object wrapper bound to the same exact native identity.</summary>
    public string? objectName { get; init; }
}
