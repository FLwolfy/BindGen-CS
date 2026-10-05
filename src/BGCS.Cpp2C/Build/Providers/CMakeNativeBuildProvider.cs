using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BGCS.Cpp2C.Build.Providers;

/// <summary>
/// Produces portable configure/build pipelines backed by CMake.
/// </summary>
public sealed class CMakeNativeBuildProvider : INativeBuildPipelineProvider
{
    private readonly string m_cmakePath;
    private readonly string? m_compilerPath;
    /// <summary>
    /// Creates a native build provider with explicitly selected tool commands.
    /// </summary>
    /// <param name="cmakePath">CMake command or absolute path.</param>
    /// <param name="compilerPath">Optional compiler override; null uses CMake toolchain selection.</param>
    public CMakeNativeBuildProvider(
        string cmakePath = "cmake",
        string? compilerPath = null
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(cmakePath);
        this.m_cmakePath = cmakePath;
        this.m_compilerPath = compilerPath;
    }

    /// <inheritdoc/>
    public string name => "cmake";

    /// <inheritdoc/>
    public NativeBuildPipeline CreatePipeline(
        CppBridgeBuildManifest manifest,
        string manifestPath,
        string? outputPath = null
    ) {
        ArgumentNullException.ThrowIfNull(manifest);
        string root = NativeBuildPaths.GetManifestDirectory(manifestPath);
        string artifact = NativeBuildPaths.GetOutputPath(manifest, manifestPath, outputPath);
        string projectDirectory = Path.Combine(root, ".bgcs", "cmake-project");
        string buildDirectory = Path.Combine(root, ".bgcs", "cmake-build");
        List<string> configure = ["-S", projectDirectory, "-B", buildDirectory, "-DCMAKE_BUILD_TYPE=Release"];
        string? selectedCompiler = this.m_compilerPath ?? manifest.compilerPath;
        if (!string.IsNullOrWhiteSpace(selectedCompiler))
            configure.Add("-DCMAKE_CXX_COMPILER=" + NativeBuildPaths.ResolveTool(root, selectedCompiler));
        if (!string.IsNullOrWhiteSpace(manifest.targetTriple) && !string.IsNullOrWhiteSpace(selectedCompiler) && NativeCompilerTargeting.AcceptsClangTarget(selectedCompiler))
            configure.Add("-DCMAKE_CXX_COMPILER_TARGET=" + manifest.targetTriple);
        if (!string.IsNullOrWhiteSpace(manifest.targetSysRoot))
            configure.Add("-DCMAKE_SYSROOT=" + NativeBuildPaths.Resolve(root, manifest.targetSysRoot));
        return new(this.name, [new(Path.Combine(projectDirectory, "CMakeLists.txt"), CreateProject(manifest, root, artifact))], [new("configure", this.m_cmakePath, configure.AsReadOnly(), root), new("build", this.m_cmakePath, new[] { "--build", buildDirectory, "--config", "Release" }, root)], artifact);
    }

    private static string CreateProject(
        CppBridgeBuildManifest manifest,
        string root,
        string artifact
    ) {
        StringBuilder text = new();
        text.AppendLine("cmake_minimum_required(VERSION 3.20)");
        text.AppendLine("project(bgcs_bridge LANGUAGES CXX)");
        text.Append("add_library(bgcs_bridge SHARED");
        foreach (string source in manifest.sourceFiles)
            text.AppendLine().Append("  \"").Append(Escape(NativeBuildPaths.Resolve(root, source))).Append('"');
        text.AppendLine().AppendLine(")");
        string standard = new(manifest.languageStandard.Where(char.IsDigit).ToArray());
        text.Append("set_property(TARGET bgcs_bridge PROPERTY CXX_STANDARD ").Append(string.IsNullOrEmpty(standard) ? "23" : standard).AppendLine(")");
        text.AppendLine("set_property(TARGET bgcs_bridge PROPERTY CXX_STANDARD_REQUIRED ON)");
        text.Append("set_target_properties(bgcs_bridge PROPERTIES OUTPUT_NAME \"").Append(Escape(NativeBuildPaths.GetLogicalLibraryName(artifact))).AppendLine("\")");
        text.Append("set_target_properties(bgcs_bridge PROPERTIES LIBRARY_OUTPUT_DIRECTORY \"").Append(Escape(Path.GetDirectoryName(artifact)!)).AppendLine("\")");
        text.Append("set_target_properties(bgcs_bridge PROPERTIES RUNTIME_OUTPUT_DIRECTORY \"").Append(Escape(Path.GetDirectoryName(artifact)!)).AppendLine("\")");
        text.Append("set_target_properties(bgcs_bridge PROPERTIES LIBRARY_OUTPUT_DIRECTORY_RELEASE \"").Append(Escape(Path.GetDirectoryName(artifact)!)).AppendLine("\")");
        text.Append("set_target_properties(bgcs_bridge PROPERTIES RUNTIME_OUTPUT_DIRECTORY_RELEASE \"").Append(Escape(Path.GetDirectoryName(artifact)!)).AppendLine("\")");
        if (manifest.includeDirectories.Count + manifest.systemIncludeDirectories.Count > 0)
        {
            text.Append("target_include_directories(bgcs_bridge PRIVATE");
            foreach (string include in manifest.includeDirectories.Concat(manifest.systemIncludeDirectories))
                text.AppendLine().Append("  \"").Append(Escape(NativeBuildPaths.Resolve(root, include))).Append('"');
            text.AppendLine().AppendLine(")");
        }

        if (manifest.defines.Count > 0)
            text.Append("target_compile_definitions(bgcs_bridge PRIVATE ").Append(string.Join(' ', manifest.defines.Select(EscapeToken))).AppendLine(")");
        IEnumerable<string> compilerArguments = manifest.compilerArguments.Where(argument => !argument.StartsWith("-std=", StringComparison.Ordinal));
        if (compilerArguments.Any())
            text.Append("target_compile_options(bgcs_bridge PRIVATE ").Append(string.Join(' ', compilerArguments.Select(Quote))).AppendLine(")");
        List<string> linkOptions = manifest.librarySearchDirectories.Select(directory => "-L" + NativeBuildPaths.Resolve(root, directory)).Concat(manifest.linkerArguments).ToList();
        if (linkOptions.Count > 0)
            text.Append("target_link_options(bgcs_bridge PRIVATE ").Append(string.Join(' ', linkOptions.Select(Quote))).AppendLine(")");
        if (manifest.linkLibraries.Count > 0)
            text.Append("target_link_libraries(bgcs_bridge PRIVATE ").Append(string.Join(' ', manifest.linkLibraries.Select(library => Quote(NormalizeLibrary(root, library))))).AppendLine(")");
        return text.ToString();
    }

    private static string NormalizeLibrary(
        string root,
        string library
    ) => Path.IsPathRooted(library) || library.Contains('/') || library.Contains('\\') ? NativeBuildPaths.Resolve(root, library) : library;
    private static string Escape(string value) => value.Replace("\\", "/", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    private static string EscapeToken(string value) => value.Replace(";", "\\;", StringComparison.Ordinal);
    private static string Quote(string value) => "\"" + Escape(value) + "\"";
}
