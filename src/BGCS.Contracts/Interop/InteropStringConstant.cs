using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Contracts.Interop;

/// <summary>Preserves a selected literal's exact native units without inventing a runtime linker symbol or an encoding.</summary>
/// <param name="sourceDeclaration">The originating native identity.</param>
/// <param name="name">The native name.</param>
/// <param name="qualifiedName">The exact source selector.</param>
/// <param name="value">The compiler-established literal and code units.</param>
/// <param name="documentation">Detached source documentation.</param>
public sealed record InteropStringConstant(
    StableId sourceDeclaration,
    string name,
    string qualifiedName,
    NativeStringConstant value,
    string? documentation
);
