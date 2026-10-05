namespace BGCS.Cpp2C.Lowering;

/// <summary>One C ABI parameter contributed by a type lowering.</summary>
/// <param name = "nameSuffix">Suffix appended to the native parameter name, or an empty string for the primary value.</param>
/// <param name = "cAbiType">C-compatible parameter type.</param>
public sealed record CppAbiParameter(
    string nameSuffix,
    string cAbiType
);
