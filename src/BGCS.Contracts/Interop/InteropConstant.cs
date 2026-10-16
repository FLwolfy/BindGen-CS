using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Contracts.Interop;

/// <summary>Preserves a selected native constant independently of callable ABI signatures.</summary>
/// <param name="sourceDeclaration">The exact originating native declaration identity.</param>
/// <param name="name">The source unqualified identifier.</param>
/// <param name="qualifiedName">The source qualified identifier used by explicit mappings.</param>
/// <param name="value">The compiler-established value and representation.</param>
/// <param name="documentation">Detached source documentation, when available.</param>
public sealed record InteropConstant(
    StableId sourceDeclaration,
    string name,
    string qualifiedName,
    NativeScalarConstant value,
    string? documentation
);
