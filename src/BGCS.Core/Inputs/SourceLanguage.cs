namespace BGCS.Core.Inputs;

/// <summary>
/// Identifies the source language independently of the parser implementation.
/// </summary>
public enum SourceLanguage
{
    /// <summary>No language has been selected.</summary>
    None,
    /// <summary>C++ declarations and expressions.</summary>
    Cpp,
    /// <summary>C declarations and expressions.</summary>
    C,
}
