namespace BGCS;

/// <summary>Chooses the C# lowering pipeline used for configured generation.</summary>
public enum CSharpEmissionBackend
{
    /// <summary>
    /// The canonical Binding IR analyzer and AST-independent C# emitter. Additional backends may be added to
    /// this selection point in the future, but the pre-release product carries no legacy backend.
    /// </summary>
    IntermediateRepresentation
}
