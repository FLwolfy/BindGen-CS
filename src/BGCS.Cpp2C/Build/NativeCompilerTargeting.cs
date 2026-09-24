namespace BGCS.Cpp2C.Build;

/// <summary>Separates Clang target selection from GNU drivers that select their target at build time.</summary>
internal static class NativeCompilerTargeting
{
    public static bool AcceptsClangTarget(string compilerPath)
    {
        string name = Path.GetFileNameWithoutExtension(compilerPath);
        return name.StartsWith("clang", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("-clang++", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("-clang", StringComparison.OrdinalIgnoreCase);
    }
}
