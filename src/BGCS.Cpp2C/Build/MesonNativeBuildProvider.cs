using System.Text;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Produces portable setup/compile pipelines backed by Meson.
/// </summary>
public sealed class MesonNativeBuildProvider : INativeBuildPipelineProvider
{
    private readonly string mesonPath;
    private readonly string? compilerPath;

    public MesonNativeBuildProvider(string mesonPath = "meson", string? compilerPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mesonPath);
        this.mesonPath = mesonPath;
        this.compilerPath = compilerPath;
    }

    public string Name => "meson";

    public NativeBuildPipeline CreatePipeline(CppBridgeBuildManifest manifest, string manifestPath, string? outputPath = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string root = NativeBuildPaths.GetManifestDirectory(manifestPath);
        string artifact = NativeBuildPaths.GetOutputPath(manifest, manifestPath, outputPath);
        string projectDirectory = Path.Combine(root, ".bgcs", "meson-project");
        string buildDirectory = Path.Combine(root, ".bgcs", "meson-build");
        string nativeFile = Path.Combine(projectDirectory, "bgcs-native.ini");
        List<NativeBuildInputFile> inputFiles = [new(Path.Combine(projectDirectory, "meson.build"), CreateProject(manifest, root, artifact))];
        List<string> setupArguments = ["setup", "--wipe", buildDirectory, projectDirectory, "--buildtype=release"];
        string? selectedCompiler = compilerPath ?? manifest.CompilerPath;
        if (!string.IsNullOrWhiteSpace(selectedCompiler))
        {
            inputFiles.Add(new(nativeFile, CreateNativeFile(NativeBuildPaths.ResolveTool(root, selectedCompiler))));
            setupArguments.Add("--native-file");
            setupArguments.Add(nativeFile);
        }
        return new(
            Name,
            inputFiles.AsReadOnly(),
            [
                new("setup", mesonPath, setupArguments.AsReadOnly(), root),
                new("compile", mesonPath, new[] { "compile", "-C", buildDirectory }, root),
                new("install", mesonPath, new[] { "install", "-C", buildDirectory, "--no-rebuild" }, root)
            ],
            artifact);
    }

    private static string CreateProject(CppBridgeBuildManifest manifest, string root, string artifact)
    {
        string standard = manifest.LanguageStandard.StartsWith("c++", StringComparison.Ordinal) ? manifest.LanguageStandard : "c++23";
        StringBuilder text = new();
        text.Append("project('bgcs_bridge', 'cpp', default_options: ['cpp_std=").Append(Escape(standard)).AppendLine("'])");
        text.AppendLine("sources = [");
        foreach (string source in manifest.SourceFiles)
            text.Append("  '").Append(Escape(NativeBuildPaths.Resolve(root, source))).AppendLine("',");
        text.AppendLine("]");
        text.AppendLine("includes = include_directories([");
        foreach (string include in manifest.IncludeDirectories.Concat(manifest.SystemIncludeDirectories))
            text.Append("  '").Append(Escape(NativeBuildPaths.Resolve(root, include))).AppendLine("',");
        text.AppendLine("])");
        IEnumerable<string> compilerArguments = manifest.Defines.Select(define => "-D" + define)
            .Concat(manifest.CompilerArguments.Where(argument => !argument.StartsWith("-std=", StringComparison.Ordinal)));
        if (!string.IsNullOrWhiteSpace(manifest.TargetTriple))
            compilerArguments = compilerArguments.Prepend("--target=" + manifest.TargetTriple);
        if (!string.IsNullOrWhiteSpace(manifest.TargetSysRoot))
            compilerArguments = compilerArguments.Prepend("--sysroot=" + NativeBuildPaths.Resolve(root, manifest.TargetSysRoot));
        IEnumerable<string> linkArguments = manifest.LibrarySearchDirectories.Select(directory => "-L" + NativeBuildPaths.Resolve(root, directory))
            .Concat(manifest.LinkLibraries.Select(library => NormalizeLibrary(root, library)))
            .Concat(manifest.LinkerArguments);
        text.AppendLine("shared_library(");
        text.Append("  '").Append(Escape(NativeBuildPaths.GetLogicalLibraryName(artifact))).AppendLine("',");
        text.AppendLine("  sources,");
        text.AppendLine("  include_directories: includes,");
        text.Append("  cpp_args: [").Append(string.Join(", ", compilerArguments.Select(Quote))).AppendLine("],");
        text.Append("  link_args: [").Append(string.Join(", ", linkArguments.Select(Quote))).AppendLine("],");
        text.Append("  name_prefix: '").Append(manifest.TargetIdentifier.StartsWith("windows-", StringComparison.OrdinalIgnoreCase) ? string.Empty : "lib").AppendLine("',");
        text.Append("  install: true,").AppendLine();
        text.Append("  install_dir: '").Append(Escape(Path.GetDirectoryName(artifact)!)).AppendLine("',");
        text.Append("  build_by_default: true,").AppendLine();
        text.Append("  override_options: ['b_lundef=false'],").AppendLine();
        text.AppendLine(")");
        return text.ToString();
    }

    private static string CreateNativeFile(string compiler) =>
        "[binaries]" + Environment.NewLine +
        "cpp = '" + Escape(compiler) + "'" + Environment.NewLine;

    private static string NormalizeLibrary(string root, string library)
    {
        if (Path.IsPathRooted(library) || library.Contains('/') || library.Contains('\\'))
            return NativeBuildPaths.Resolve(root, library);
        return library.StartsWith("-", StringComparison.Ordinal) ? library : "-l" + library;
    }

    private static string Quote(string value) => "'" + Escape(value) + "'";
    private static string Escape(string value) => value.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}
