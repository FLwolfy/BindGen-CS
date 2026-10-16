using System.Collections.Immutable;
using System.Linq;
using System;
using System.IO;
using BGCS.Contracts.Requests;

namespace BGCS.NativeBuild.Planning;

internal static class NativeBuildEnvironment
{
    internal static string ArgumentDirectory(NativeBuildRequest request)
    {
        string directory = request.argumentWorkingDirectory ?? request.workingDirectory;
        if (!Path.IsPathFullyQualified(directory) || !Path.IsPathFullyQualified(request.workingDirectory) || !Path.IsPathFullyQualified(request.outputFile))
            throw new ArgumentException("Native construction requires absolute argument, scratch and output paths.", nameof(request));
        return directory;
    }
    internal static ImmutableDictionary<string, string?> cleanCompiler { get; } =
        new[] { "CL", "_CL_", "LINK", "_LINK_", "LIB", "INCLUDE", "CPATH", "C_INCLUDE_PATH", "CPLUS_INCLUDE_PATH", "OBJC_INCLUDE_PATH", "LIBRARY_PATH", "COMPILER_PATH", "GCC_EXEC_PREFIX", "CCC_OVERRIDE_OPTIONS" }
        .ToImmutableDictionary(name => name, _ => (string?)null, System.StringComparer.Ordinal);
}
