using System.Text;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Produces portable configure/build pipelines backed by CMake.
/// </summary>
public sealed class CMakeNativeBuildProvider : INativeBuildPipelineProvider
{
    private readonly string cmakePath;
    private readonly string? compilerPath;

    public CMakeNativeBuildProvider(string cmakePath = "cmake", string? compilerPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cmakePath);
        this.cmakePath = cmakePath;
        this.compilerPath = compilerPath;
    }

    public string Name => "cmake";

    public NativeBuildPipeline CreatePipeline(CppBridgeBuildManifest manifest, string manifestPath, string? outputPath = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string root = NativeBuildPaths.GetManifestDirectory(manifestPath);
        string artifact = NativeBuildPaths.GetOutputPath(manifest, manifestPath, outputPath);
        string projectDirectory = Path.Combine(root, ".bgcs", "cmake-project");
        string buildDirectory = Path.Combine(root, ".bgcs", "cmake-build");
        List<string> configure = ["-S", projectDirectory, "-B", buildDirectory, "-DCMAKE_BUILD_TYPE=Release"];
        string? selectedCompiler = compilerPath ?? manifest.CompilerPath;
        if (!string.IsNullOrWhiteSpace(selectedCompiler))
            configure.Add("-DCMAKE_CXX_COMPILER=" + NativeBuildPaths.ResolveTool(root, selectedCompiler));
        if (!string.IsNullOrWhiteSpace(manifest.TargetTriple) &&
            !string.IsNullOrWhiteSpace(selectedCompiler) && NativeCompilerTargeting.AcceptsClangTarget(selectedCompiler))
            configure.Add("-DCMAKE_CXX_COMPILER_TARGET=" + manifest.TargetTriple);
        if (!string.IsNullOrWhiteSpace(manifest.TargetSysRoot))
            configure.Add("-DCMAKE_SYSROOT=" + NativeBuildPaths.Resolve(root, manifest.TargetSysRoot));
        return new(
            Name,
            [new(Path.Combine(projectDirectory, "CMakeLists.txt"), CreateProject(manifest, root, artifact))],
            [
                new("configure", cmakePath, configure.AsReadOnly(), root),
                new("build", cmakePath, new[] { "--build", buildDirectory, "--config", "Release" }, root)
            ],
            artifact);
    }

    private static string CreateProject(CppBridgeBuildManifest manifest, string root, string artifact)
    {
        StringBuilder text = new();
        text.AppendLine("cmake_minimum_required(VERSION 3.20)");
        text.AppendLine("project(bgcs_bridge LANGUAGES CXX)");
        text.Append("add_library(bgcs_bridge SHARED");
        foreach (string source in manifest.SourceFiles)
            text.AppendLine().Append("  \"").Append(Escape(NativeBuildPaths.Resolve(root, source))).Append('"');
        text.AppendLine().AppendLine(")");
        string standard = new(manifest.LanguageStandard.Where(char.IsDigit).ToArray());
        text.Append("set_property(TARGET bgcs_bridge PROPERTY CXX_STANDARD ").Append(string.IsNullOrEmpty(standard) ? "23" : standard).AppendLine(")");
        text.AppendLine("set_property(TARGET bgcs_bridge PROPERTY CXX_STANDARD_REQUIRED ON)");
        text.Append("set_target_properties(bgcs_bridge PROPERTIES OUTPUT_NAME \"")
            .Append(Escape(NativeBuildPaths.GetLogicalLibraryName(artifact))).AppendLine("\")");
        text.Append("set_target_properties(bgcs_bridge PROPERTIES LIBRARY_OUTPUT_DIRECTORY \"")
            .Append(Escape(Path.GetDirectoryName(artifact)!)).AppendLine("\")");
        text.Append("set_target_properties(bgcs_bridge PROPERTIES RUNTIME_OUTPUT_DIRECTORY \"")
            .Append(Escape(Path.GetDirectoryName(artifact)!)).AppendLine("\")");
        text.Append("set_target_properties(bgcs_bridge PROPERTIES LIBRARY_OUTPUT_DIRECTORY_RELEASE \"")
            .Append(Escape(Path.GetDirectoryName(artifact)!)).AppendLine("\")");
        text.Append("set_target_properties(bgcs_bridge PROPERTIES RUNTIME_OUTPUT_DIRECTORY_RELEASE \"")
            .Append(Escape(Path.GetDirectoryName(artifact)!)).AppendLine("\")");
        if (manifest.IncludeDirectories.Count + manifest.SystemIncludeDirectories.Count > 0)
        {
            text.Append("target_include_directories(bgcs_bridge PRIVATE");
            foreach (string include in manifest.IncludeDirectories.Concat(manifest.SystemIncludeDirectories))
                text.AppendLine().Append("  \"").Append(Escape(NativeBuildPaths.Resolve(root, include))).Append('"');
            text.AppendLine().AppendLine(")");
        }
        if (manifest.Defines.Count > 0)
            text.Append("target_compile_definitions(bgcs_bridge PRIVATE ").Append(string.Join(' ', manifest.Defines.Select(EscapeToken))).AppendLine(")");
        IEnumerable<string> compilerArguments = manifest.CompilerArguments.Where(argument => !argument.StartsWith("-std=", StringComparison.Ordinal));
        if (compilerArguments.Any())
            text.Append("target_compile_options(bgcs_bridge PRIVATE ").Append(string.Join(' ', compilerArguments.Select(Quote))).AppendLine(")");
        List<string> linkOptions = manifest.LibrarySearchDirectories.Select(directory => "-L" + NativeBuildPaths.Resolve(root, directory))
            .Concat(manifest.LinkerArguments).ToList();
        if (linkOptions.Count > 0)
            text.Append("target_link_options(bgcs_bridge PRIVATE ").Append(string.Join(' ', linkOptions.Select(Quote))).AppendLine(")");
        if (manifest.LinkLibraries.Count > 0)
            text.Append("target_link_libraries(bgcs_bridge PRIVATE ").Append(string.Join(' ', manifest.LinkLibraries.Select(library => Quote(NormalizeLibrary(root, library))))).AppendLine(")");
        return text.ToString();
    }

    private static string NormalizeLibrary(string root, string library) =>
        Path.IsPathRooted(library) || library.Contains('/') || library.Contains('\\')
            ? NativeBuildPaths.Resolve(root, library)
            : library;

    private static string Escape(string value) => value.Replace("\\", "/", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    private static string EscapeToken(string value) => value.Replace(";", "\\;", StringComparison.Ordinal);
    private static string Quote(string value) => "\"" + Escape(value) + "\"";
}
