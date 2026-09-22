namespace BGCS.Cpp2C.Build;

internal static class NativeBuildPaths
{
    public static string GetManifestDirectory(string manifestPath)
    {
        string fullManifestPath = Path.GetFullPath(manifestPath);
        return Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidOperationException($"Cannot determine manifest directory for '{fullManifestPath}'.");
    }

    public static string GetOutputPath(CppBridgeBuildManifest manifest, string manifestPath, string? outputPath)
    {
        string manifestDirectory = GetManifestDirectory(manifestPath);
        return string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(manifestDirectory, "bin", GetLibraryFileName(manifest.TargetIdentifier, manifest.LibraryName))
            : Path.GetFullPath(outputPath, manifestDirectory);
    }

    public static string Resolve(string manifestDirectory, string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, manifestDirectory);

    public static string ResolveTool(string manifestDirectory, string tool) =>
        Path.IsPathRooted(tool) || tool.Contains('/') || tool.Contains('\\')
            ? Resolve(manifestDirectory, tool)
            : tool;

    public static string GetLibraryFileName(string targetIdentifier, string libraryName)
    {
        string fileName = Path.GetFileName(libraryName);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException("LibraryName must contain a valid file name.");
        if (targetIdentifier.StartsWith("windows-", StringComparison.OrdinalIgnoreCase))
            return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".dll";
        if (targetIdentifier.StartsWith("macos-", StringComparison.OrdinalIgnoreCase) ||
            targetIdentifier.StartsWith("ios-", StringComparison.OrdinalIgnoreCase))
            return WithUnixLibraryName(fileName, ".dylib");
        return WithUnixLibraryName(fileName, ".so");
    }

    public static string GetLogicalLibraryName(string outputFile)
    {
        string name = Path.GetFileNameWithoutExtension(outputFile);
        return name.StartsWith("lib", StringComparison.Ordinal) ? name[3..] : name;
    }

    private static string WithUnixLibraryName(string fileName, string extension)
    {
        if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            return fileName;
        return (fileName.StartsWith("lib", StringComparison.Ordinal) ? fileName : "lib" + fileName) + extension;
    }
}
