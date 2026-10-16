using System;

namespace BGCS.CSharp.Emission;

internal static class ManagedPrimitiveCatalog
{
    internal static bool IsKnownPrimitive(string name)
    {
        ReadOnlySpan<char> baseName = name.AsSpan().Trim();
        while (baseName.EndsWith("*", StringComparison.Ordinal))
            baseName = baseName[..^1].TrimEnd();
        return baseName is "void" or "bool" or "byte" or "sbyte" or "char"
            or "short" or "ushort" or "int" or "uint" or "long" or "ulong"
            or "float" or "double" or "nint" or "nuint";
    }
}
