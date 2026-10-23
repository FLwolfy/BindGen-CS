using System.Text.Json.Serialization;

namespace BGCS.Core.Targets;

/// <summary>Supplies explicit target facts for independently checking native callable declarations.</summary>
/// <param name="defaultCConvention">The established default C convention: C, Win64 or X86_64SysV. This is a native convention fact, not a platform name or a verification claim.</param>
/// <param name="permitsDirectCallOnlyVoidIntrinsic">Whether a declared addressless C void(void) intrinsic may use the implemented compiler-diagnostic redeclaration proof. Other signatures remain unsupported by that proof.</param>
public sealed record NativeCallableAssertionFacts(
    [property: JsonRequired] string defaultCConvention,
    [property: JsonRequired] bool permitsDirectCallOnlyVoidIntrinsic
);
