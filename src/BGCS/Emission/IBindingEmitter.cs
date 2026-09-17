namespace BGCS.Emission;

using BGCS.Intermediate;

/// <summary>
/// Emits one backend representation from a fully analyzed binding module.
/// </summary>
public interface IBindingEmitter
{
    string Name { get; }
    IReadOnlyList<string> Emit(BindingModule module, EmissionContext context);
}

/// <summary>
/// Provides immutable output settings to emission backends.
/// </summary>
/// <param name="OutputPath">Staging output directory.</param>
/// <param name="SingleFile">Whether backend files should be composed into one source file.</param>
/// <param name="SingleFileName">Requested single-file name.</param>
public sealed record EmissionContext(string OutputPath, bool SingleFile, string SingleFileName,
    string RuntimeNamespace = "BGCS.Runtime");
