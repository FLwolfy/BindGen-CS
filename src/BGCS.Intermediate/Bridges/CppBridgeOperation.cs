namespace BGCS.Intermediate.Bridges;

/// <summary>
/// Identifies a lowered source operation whose formatting is performed by the bridge emitter.
/// </summary>
public enum CppBridgeOperationKind
{
    /// <summary>
    /// Appends a lowered syntax fragment without terminating the current line.
    /// </summary>
    Fragment,
    /// <summary>
    /// Appends a lowered syntax fragment and terminates the current line.
    /// </summary>
    Line,
    /// <summary>
    /// Begins a declaration or statement block with the supplied header.
    /// </summary>
    BeginBlock,
    /// <summary>
    /// Ends the current block with its lowered closing marker.
    /// </summary>
    EndBlock,
    /// <summary>
    /// Increases indentation for subsequent operations.
    /// </summary>
    Indent,
    /// <summary>
    /// Decreases indentation for subsequent operations.
    /// </summary>
    Unindent
}

/// <summary>
/// Stores lowered C or C++ syntax independently of the source AST and lowering services.
/// </summary>
/// <param name = "kind">
/// The source operation to perform.
/// </param>
/// <param name = "syntax">
/// The resolved declaration, expression, statement or closing marker; no templates remain unresolved.
/// </param>
/// <param name = "count">
/// The indentation change for explicit indentation operations.
/// </param>
public sealed record CppBridgeOperation(
    CppBridgeOperationKind kind,
    string syntax = "",
    int count = 1
);
