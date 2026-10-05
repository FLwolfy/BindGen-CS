namespace BGCS.Intermediate.Emission;

/// <summary>
/// Provides output settings independently of the analyzed native ABI model.
/// </summary>
/// <param name = "outputPath">
/// The caller-owned staging directory into which the backend writes source files.
/// </param>
/// <param name = "singleFile">
/// Whether the backend combines its source declarations into one file.
/// </param>
/// <param name = "singleFileName">
/// The source file name used when single-file output is requested.
/// </param>
/// <param name = "runtimeNamespace">
/// The namespace containing the consumer's interop runtime types.
/// </param>
/// <param name = "oneFilePerType">
/// Whether split output assigns each generated type a separate file.
/// </param>
public sealed record EmissionContext(
    string outputPath,
    bool singleFile,
    string singleFileName,
    string runtimeNamespace = "BGCS.Runtime",
    bool oneFilePerType = false
);
