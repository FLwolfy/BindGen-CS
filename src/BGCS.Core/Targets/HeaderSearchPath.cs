namespace BGCS.Core.Targets;

/// <summary>
/// Describes a compiler-discovered include path without retaining compiler services.
/// </summary>
/// <param name="path">The absolute include search directory.</param>
/// <param name="isBuiltin">Whether the directory belongs to the driver's builtin headers.</param>
/// <param name="isFramework">Whether the directory is an Apple framework search root.</param>
public readonly record struct HeaderSearchPath(
    string path,
    bool isBuiltin,
    bool isFramework
);
