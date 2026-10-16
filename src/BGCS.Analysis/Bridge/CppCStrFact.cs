using BGCS.Contracts.Common;

namespace BGCS.Analysis.Bridge;

/// <summary>Identifies the actual accessible native c_str member without inferring encoding or readable lifetime.</summary>
/// <param name="member">The compiler-derived member declaration identity.</param>
/// <param name="elementType">The exact unqualified native code-unit identity.</param>
/// <param name="isConst">Whether the member can be invoked through a const receiver.</param>
public sealed record CppCStrFact(
    StableId member,
    StableId elementType,
    bool isConst
);
