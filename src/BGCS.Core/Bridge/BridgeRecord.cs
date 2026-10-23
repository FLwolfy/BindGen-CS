using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Bridge;

/// <summary>Preserves compiler-established DTO storage without enabling aggregate-by-value C interop.</summary>
/// <param name="size">The complete original native byte size.</param>
/// <param name="alignment">The original native storage alignment.</param>
/// <param name="fields">The complete accessible nonstatic fields in native declaration order.</param>
public sealed record BridgeRecord(
    long size,
    int alignment,
    ImmutableArray<BridgeRecordField> fields
);

/// <summary>Associates one DTO field with its exact native origin and checked C storage type.</summary>
/// <param name="declaration">The originating native field identity.</param>
/// <param name="name">The original accessible field name.</param>
/// <param name="type">The checked bridge storage identity, including nested records or fixed arrays.</param>
/// <param name="offset">The compiler-established byte offset.</param>
public sealed record BridgeRecordField(
    StableId declaration,
    string name,
    StableId type,
    long offset
);
