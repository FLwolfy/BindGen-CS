namespace BGCS.Tool;

/// <summary>
/// Compares generated artifacts while treating the ABI reference target as provenance,
/// not as a change to the C# binding contract.
/// </summary>
internal static class GeneratedSourceComparison
{
    private const string ReferenceMarker = "ABI reference target: ";

    public static bool Equals(string path, string expected, string actual)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return string.Equals(expected, actual, StringComparison.Ordinal);
        return string.Equals(Normalize(expected), Normalize(actual), StringComparison.Ordinal);
    }

    private static string Normalize(string source)
    {
        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int index = 0; index < Math.Min(lines.Length, 12); index++)
        {
            string line = lines[index];
            int marker = line.IndexOf(ReferenceMarker, StringComparison.Ordinal);
            if (marker >= 0 && line[..marker].TrimStart().StartsWith("//", StringComparison.Ordinal))
                lines[index] = line[..(marker + ReferenceMarker.Length)] + "<target>";
        }
        return string.Join('\n', lines);
    }
}
