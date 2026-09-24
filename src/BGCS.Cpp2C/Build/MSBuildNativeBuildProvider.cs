using System.Security;
using System.Text;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Produces Windows Visual C++ project build pipelines backed by MSBuild.
/// </summary>
public sealed class MSBuildNativeBuildProvider : INativeBuildPipelineProvider
{
    private readonly string msbuildPath;

    public MSBuildNativeBuildProvider(string msbuildPath = "msbuild")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msbuildPath);
        this.msbuildPath = msbuildPath;
    }

    public string Name => "msbuild";

    public NativeBuildPipeline CreatePipeline(CppBridgeBuildManifest manifest, string manifestPath, string? outputPath = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!manifest.TargetIdentifier.StartsWith("windows-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MSBuild can only build Windows bridge targets.");
        string root = NativeBuildPaths.GetManifestDirectory(manifestPath);
        string artifact = NativeBuildPaths.GetOutputPath(manifest, manifestPath, outputPath);
        string projectDirectory = Path.Combine(root, ".bgcs", "msbuild-project");
        string projectPath = Path.Combine(projectDirectory, "BGCS.Bridge.vcxproj");
        string platform = GetPlatform(manifest.TargetIdentifier);
        string outputDirectory = Path.GetDirectoryName(artifact)! + Path.DirectorySeparatorChar;
        IReadOnlyList<string> arguments =
        [
            projectPath,
            "-nologo",
            "-m",
            "-t:Build",
            "-p:Configuration=Release",
            "-p:Platform=" + platform,
            "-p:PlatformToolset=v143",
            "-p:OutDir=" + outputDirectory,
            "-p:TargetName=" + NativeBuildPaths.GetLogicalLibraryName(artifact),
            "-p:TargetExt=.dll"
        ];
        return new(
            Name,
            [new(projectPath, CreateProject(manifest, root, platform))],
            [new("build", msbuildPath, arguments, root)],
            artifact);
    }

    private static string CreateProject(CppBridgeBuildManifest manifest, string root, string platform)
    {
        string includeDirectories = string.Join(';', manifest.IncludeDirectories.Concat(manifest.SystemIncludeDirectories)
            .Select(path => NativeBuildPaths.Resolve(root, path)).Append("%(AdditionalIncludeDirectories)"));
        string preprocessorDefinitions = string.Join(';', manifest.Defines.Append("%(PreprocessorDefinitions)"));
        string compilerArguments = string.Join(' ', manifest.CompilerArguments.Where(argument =>
            !argument.StartsWith("-std=", StringComparison.Ordinal)).Append("%(AdditionalOptions)"));
        string libraryDirectories = string.Join(';', manifest.LibrarySearchDirectories
            .Select(path => NativeBuildPaths.Resolve(root, path)).Append("%(AdditionalLibraryDirectories)"));
        string dependencies = string.Join(';', manifest.LinkLibraries.Select(library => NormalizeLibrary(root, library))
            .Append("%(AdditionalDependencies)"));
        string linkerArguments = string.Join(' ', manifest.LinkerArguments.Append("%(AdditionalOptions)"));
        StringBuilder text = new();
        text.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        text.AppendLine("<Project DefaultTargets=\"Build\" ToolsVersion=\"Current\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
        text.AppendLine("  <ItemGroup Label=\"ProjectConfigurations\">");
        text.Append("    <ProjectConfiguration Include=\"Release|").Append(Xml(platform)).AppendLine("\">");
        text.AppendLine("      <Configuration>Release</Configuration>");
        text.Append("      <Platform>").Append(Xml(platform)).AppendLine("</Platform>");
        text.AppendLine("    </ProjectConfiguration>");
        text.AppendLine("  </ItemGroup>");
        text.AppendLine("  <PropertyGroup Label=\"Globals\">");
        text.AppendLine("    <ProjectGuid>{AB8A106D-D06F-4863-A77E-A4C65F9AE78B}</ProjectGuid>");
        text.AppendLine("    <Keyword>Win32Proj</Keyword>");
        text.AppendLine("  </PropertyGroup>");
        text.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.Default.props\" />");
        text.AppendLine("  <PropertyGroup Condition=\"'$(Configuration)|$(Platform)'=='Release|")
            .Append(Xml(platform)).AppendLine("'\" Label=\"Configuration\">");
        text.AppendLine("    <ConfigurationType>DynamicLibrary</ConfigurationType>");
        text.AppendLine("    <PlatformToolset>v143</PlatformToolset>");
        text.AppendLine("    <CharacterSet>Unicode</CharacterSet>");
        text.AppendLine("  </PropertyGroup>");
        text.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.props\" />");
        text.AppendLine("  <ItemDefinitionGroup>");
        text.AppendLine("    <ClCompile>");
        text.Append("      <AdditionalIncludeDirectories>").Append(Xml(includeDirectories)).AppendLine("</AdditionalIncludeDirectories>");
        text.Append("      <PreprocessorDefinitions>").Append(Xml(preprocessorDefinitions)).AppendLine("</PreprocessorDefinitions>");
        text.Append("      <AdditionalOptions>").Append(Xml(compilerArguments)).AppendLine("</AdditionalOptions>");
        text.AppendLine("      <LanguageStandard>stdcpplatest</LanguageStandard>");
        text.AppendLine("      <ExceptionHandling>Sync</ExceptionHandling>");
        text.AppendLine("    </ClCompile>");
        text.AppendLine("    <Link>");
        text.Append("      <AdditionalLibraryDirectories>").Append(Xml(libraryDirectories)).AppendLine("</AdditionalLibraryDirectories>");
        text.Append("      <AdditionalDependencies>").Append(Xml(dependencies)).AppendLine("</AdditionalDependencies>");
        text.Append("      <AdditionalOptions>").Append(Xml(linkerArguments)).AppendLine("</AdditionalOptions>");
        text.AppendLine("    </Link>");
        text.AppendLine("  </ItemDefinitionGroup>");
        text.AppendLine("  <ItemGroup>");
        foreach (string source in manifest.SourceFiles)
            text.Append("    <ClCompile Include=\"").Append(Xml(NativeBuildPaths.Resolve(root, source))).AppendLine("\" />");
        text.AppendLine("  </ItemGroup>");
        text.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.targets\" />");
        text.AppendLine("</Project>");
        return text.ToString();
    }

    private static string NormalizeLibrary(string root, string library)
    {
        if (Path.IsPathRooted(library) || library.Contains('/') || library.Contains('\\'))
            return NativeBuildPaths.Resolve(root, library);
        return Path.HasExtension(library) ? library : library + ".lib";
    }

    private static string GetPlatform(string targetIdentifier) => targetIdentifier.Contains("arm64", StringComparison.OrdinalIgnoreCase)
        ? "ARM64"
        : targetIdentifier.Contains("x86", StringComparison.OrdinalIgnoreCase) && !targetIdentifier.Contains("x64", StringComparison.OrdinalIgnoreCase)
            ? "Win32"
            : "x64";

    private static string Xml(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
