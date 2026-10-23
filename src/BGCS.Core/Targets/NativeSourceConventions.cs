using System.Text.Json.Serialization;

namespace BGCS.Core.Targets;

/// <summary>Supplies target-owned source spellings without embedding an operating-system dispatch table in source emitters.</summary>
/// <param name="exportDecoration">The exact declaration decoration required for a public binding export.</param>
/// <param name="cdeclDecoration">The exact source spelling for the bridge's C calling convention, or an empty spelling when it is the target default.</param>
/// <param name="callableAssertions">The target-owned calling-convention and addressless-call assertion facts.</param>
public sealed record NativeSourceConventions(
    [property: JsonRequired] string exportDecoration,
    [property: JsonRequired] string cdeclDecoration,
    [property: JsonRequired] NativeCallableAssertionFacts callableAssertions
);
