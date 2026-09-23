namespace BGCS;

/// <summary>Chooses the C# lowering pipeline used for configured generation.</summary>
public enum CSharpEmissionBackend
{
    /// <summary>The mature compatibility pipeline retained while exact IR parity is completed.</summary>
    Compatibility,

    /// <summary>The canonical Binding IR analyzer and AST-independent C# emitter.</summary>
    IntermediateRepresentation
}
