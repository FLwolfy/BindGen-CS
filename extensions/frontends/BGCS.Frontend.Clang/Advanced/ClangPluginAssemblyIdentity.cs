using System.Collections.Immutable;
using System.Reflection;

namespace BGCS.Frontend.Clang.Advanced;

/// <summary>Identifies the pinned frontend assemblies shared with advanced parser plugins.</summary>
public static class ClangPluginAssemblyIdentity
{
    /// <summary>Gets the frontend and parser assemblies whose exact loaded identities a worker must preserve.</summary>
    /// <remarks>The immutable set supplies plugin loading policy without exposing parser types to the worker implementation.</remarks>
    public static ImmutableArray<Assembly> sharedAssemblies { get; } =
    [
        typeof(IClangFactContributor).Assembly,
        typeof(ClangSharp.Interop.CXCursor).Assembly,
        typeof(ClangSharp.Cursor).Assembly,
    ];
}
