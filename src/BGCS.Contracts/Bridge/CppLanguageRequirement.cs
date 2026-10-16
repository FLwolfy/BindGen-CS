namespace BGCS.Contracts.Bridge;

/// <summary>Identifies a minimum C++ standard required by one finite generated bridge feature.</summary>
/// <param name="feature">The stable generated feature identity.</param>
/// <param name="minimumStandard">The standard suffix, for example 11, 20 or 23.</param>
public sealed record CppLanguageRequirement(
    string feature,
    int minimumStandard
);
