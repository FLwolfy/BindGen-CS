using System;
using System.Globalization;
using BGCS.Contracts.Bridge;

namespace BGCS.Cpp2C.Planning;

/// <summary>Checks the chosen source dialect against immutable requirements derived from typed bridge operations.</summary>
public sealed class CppBridgeLanguageValidator
{
    /// <summary>Rejects a selected language standard that cannot express the generated native operations.</summary>
    /// <param name="module">The frozen bridge with its derived language requirements.</param>
    /// <param name="standard">The explicit configured C++ or GNU C++ dialect.</param>
    public void Validate(
        CppBridgeModule module,
        string standard
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(standard);
        string version = standard.StartsWith("gnu++", StringComparison.Ordinal) ? standard[5..]
            : standard.StartsWith("c++", StringComparison.Ordinal) ? standard[3..]
            : throw new ArgumentException("A bridge language requirement needs an explicit C++ dialect.", nameof(standard));
        int selected = version switch
        {
            "98" => 1998,
            "03" => 2003,
            "0x" => 2011,
            "1y" => 2014,
            "1z" => 2017,
            "2a" => 2020,
            "2b" => 2023,
            "2c" => 2026,
            _ when int.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out int year) && year is >= 11 and <= 99 => 2000 + year,
            _ => throw new ArgumentException("The selected C++ standard cannot be compared to explicit bridge language requirements.", nameof(standard))
        };
        foreach (CppLanguageRequirement requirement in module.languageRequirements)
            if (selected < 2000 + requirement.minimumStandard)
                throw new NotSupportedException($"Bridge feature '{requirement.feature}' requires C++{requirement.minimumStandard} or later; selected '{standard}'.");
    }
}
