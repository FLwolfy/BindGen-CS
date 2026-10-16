using System.Collections.Immutable;
using BGCS.Contracts.Common;

namespace BGCS.Contracts.Interop;

/// <summary>Describes one final C ABI export without import mode or managed syntax.</summary>
/// <param name="id">The stable export identity.</param>
/// <param name="exportName">The exact native linker symbol.</param>
/// <param name="returnType">The native return type.</param>
/// <param name="parameters">The native parameters in ABI order.</param>
/// <param name="callingConvention">The native calling convention.</param>
public sealed record InteropFunction(
    StableId id,
    string exportName,
    StableId returnType,
    ImmutableArray<InteropParameter> parameters,
    string callingConvention
)
{
    /// <summary>Gets the original callable location independently of generated bridge source paths.</summary>
    public SourceLocation? source { get; init; }
    /// <summary>Gets the preserved primary export for an explicitly requested alternate input representation.</summary>
    public StableId? inputAdapterOf { get; init; }
    /// <summary>Gets the original source callable identity.</summary>
    public StableId? sourceDeclaration { get; init; }
    /// <summary>Gets the qualified original C++ callable name independently of its allocated C linker symbol.</summary>
    public string? sourceQualifiedName { get; init; }
    /// <summary>Gets the deterministic original overload identity including source parameter types and member qualifiers.</summary>
    public string? sourceSignature { get; init; }
    /// <summary>Gets the original unqualified source name or the finite generated native operation name.</summary>
    public string? sourceName { get; init; }
    /// <summary>Gets the detached native callable or generated bridge operation role.</summary>
    public string? sourceRole { get; init; }
    /// <summary>Gets the original native class identity for lowered members, construction or destruction.</summary>
    public StableId? sourceOwnerType { get; init; }
    /// <summary>Gets whether the callable requires a fixed-signature vararg bridge.</summary>
    public bool isVariadic { get; init; }
    /// <summary>Gets whether the export declares an exact native prototype.</summary>
    public bool hasPrototype { get; init; } = true;
    /// <summary>Gets native callable documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets parsed documentation for the original callable, independently of generated C ABI result channels.</summary>
    public SourceDocumentation? documentationContent { get; init; }
    /// <summary>Gets the return-value ownership contract.</summary>
    public OwnershipContract? returnOwnership { get; init; }
    /// <summary>Gets the return text contract.</summary>
    public StringContract? returnString { get; init; }
    /// <summary>Gets the asynchronous completion protocol.</summary>
    public AsyncContract? asynchronous { get; init; }
    /// <summary>Gets the native error protocol.</summary>
    public ErrorContract? error { get; init; }
}

/// <summary>Records a parameter's source identity and native type before semantic projection.</summary>
/// <param name="id">The source parameter identity.</param>
/// <param name="name">The native parameter name.</param>
/// <param name="type">The C ABI type identity.</param>
public sealed record InteropParameter(
    StableId id,
    string name,
    StableId type
)
{
    /// <summary>Gets the original source parameter location when available.</summary>
    public SourceLocation? source { get; init; }
    /// <summary>Gets the original source parameter identity independently of an allocated bridge parameter.</summary>
    public StableId? sourceDeclaration { get; init; }
    /// <summary>Gets the original native parameter name when a bridge uses a different C identifier.</summary>
    public string? sourceName { get; init; }
    /// <summary>Gets explicit parameter ownership.</summary>
    public OwnershipContract? ownership { get; init; }
    /// <summary>Gets explicit native text semantics.</summary>
    public StringContract? text { get; init; }
    /// <summary>Gets explicit native buffer bounds and lifetime.</summary>
    public BufferContract? buffer { get; init; }
    /// <summary>Gets explicit single-value direction and address lifetime independently of buffer bounds.</summary>
    public ValueParameterContract? singleValue { get; init; }
    /// <summary>Gets explicit callback registration semantics.</summary>
    public CallbackContract? callback { get; init; }
}
