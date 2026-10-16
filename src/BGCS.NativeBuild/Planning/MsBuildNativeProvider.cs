using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Platforms.Toolchains;

namespace BGCS.NativeBuild.Planning;

/// <summary>Executes frozen native commands through a generated MSBuild graph and an isolated shell-free process task.</summary>
public sealed class MsBuildNativeProvider : INativeBuildProvider, INativeBuildPrerequisiteProvider
{
    private readonly string m_msbuild;
    private readonly string m_tasks;
    private readonly MsvcBuildProvider m_compiler;
    /// <summary>Captures the installed MSBuild task factory and exact native SDK.</summary>
    /// <param name="msbuild">The absolute Visual Studio MSBuild executable.</param>
    /// <param name="sdk">The target-specific pinned Visual C++ and Windows SDK.</param>
    /// <param name="archiver">An optional authoritative MSVC-compatible archive tool.</param>
    public MsBuildNativeProvider(
        string msbuild,
        MsvcToolchainInfo sdk,
        string? archiver = null
    ) {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Visual C++ MSBuild tasks require a Windows host.");
        if (!Path.IsPathFullyQualified(msbuild) || !File.Exists(msbuild)) throw new FileNotFoundException("MSBuild must exist at an absolute path.", msbuild);
        m_msbuild = msbuild;
        m_tasks = Path.Combine(Path.GetDirectoryName(msbuild)!, "Microsoft.Build.Tasks.Core.dll");
        if (!File.Exists(m_tasks)) throw new FileNotFoundException("The selected MSBuild installation has no Roslyn task factory.", m_tasks);
        m_compiler = new(sdk, archiver);
    }
    /// <inheritdoc/>
    public string id => "msbuild";
    /// <inheritdoc/>
    public ImmutableArray<NativeToolPrerequisite> DescribePrerequisites() => m_compiler.DescribePrerequisites()
        .AddRange(ImmutableArray.Create(new NativeToolPrerequisite("buildSystem", "msbuild", m_msbuild), new NativeToolPrerequisite("taskAssembly", "msbuild-roslyn-tasks", m_tasks)));
    /// <inheritdoc/>
    public NativeBuildPlan Plan(
        NativeBuildRequest request,
        CancellationToken cancellationToken
    ) {
        NativeBuildPlan direct = m_compiler.Plan(request, cancellationToken);
        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        XElement project = new(ns + "Project", new XAttribute("DefaultTargets", "BGCSBuild"),
            new XElement(ns + "UsingTask", new XAttribute("TaskName", "BGCSRunFrozenNativeCommand"), new XAttribute("TaskFactory", "RoslynCodeTaskFactory"), new XAttribute("AssemblyFile", m_tasks),
                new XElement(ns + "ParameterGroup", Parameter("Executable"), Parameter("Arguments"), Parameter("WorkingDirectory")),
                new XElement(ns + "Task", new XElement(ns + "Code", new XAttribute("Type", "Fragment"), new XAttribute("Language", "cs"), new XCData(ProcessTaskCode)))));
        XElement compileTarget = new(ns + "Target", new XAttribute("Name", "BGCSCompile"));
        XElement linkTarget = new(ns + "Target", new XAttribute("Name", "BGCSLink"));
        foreach (NativeBuildCommand command in direct.commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool compile = command.id.StartsWith("compile-", StringComparison.Ordinal);
            XElement task = new(ns + "BGCSRunFrozenNativeCommand", new XAttribute("Executable", Escape(command.executable)),
                new XAttribute("Arguments", Escape(string.Join(' ', command.arguments.Select(WindowsQuote)))),
                new XAttribute("WorkingDirectory", Escape(command.workingDirectory)));
            (compile ? compileTarget : linkTarget).Add(task);
        }
        project.Add(compileTarget, linkTarget, new XElement(ns + "Target", new XAttribute("Name", "BGCSBuild"), new XAttribute("DependsOnTargets", "BGCSCompile;BGCSLink")));
        string projectFile = Path.Combine(request.workingDirectory, "msbuild-project", "Native.vcxproj");
        return direct with
        {
            providerId = id,
            generatedInputFiles = [new("msbuild-project/Native.vcxproj", new XDocument(project).ToString())],
            externalInputFiles = direct.externalInputFiles.AddRange(ImmutableArray.Create(m_msbuild, m_tasks)).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            linkDependencyScans = direct.linkDependencyScans.Select(scan => scan with { finalCommandId = "link" }).ToImmutableArray(),
            commands = [new("objects", m_msbuild, [projectFile, "-nologo", "-noAutoResponse", "-m:1", "-nr:false", "-t:BGCSCompile", "-verbosity:minimal"], request.workingDirectory) { environmentOverrides = NativeBuildEnvironment.cleanCompiler },
                new("link", m_msbuild, [projectFile, "-nologo", "-noAutoResponse", "-m:1", "-nr:false", "-t:BGCSLink", "-verbosity:minimal"], request.workingDirectory) { outputFiles = [request.outputFile], environmentOverrides = NativeBuildEnvironment.cleanCompiler, linkDependencyTrace = direct.commands[^1].linkDependencyTrace }]
        };

        XElement Parameter(string name) => new(ns + name, new XAttribute("ParameterType", "System.String"), new XAttribute("Required", "true"));
    }
    // The installed framework MSBuild process API accepts Windows argv text. This is a fixed
    // child-process adapter: WindowsQuote encodes each frozen argument, and no shell interprets it.
    private const string ProcessTaskCode = """
        var start = new System.Diagnostics.ProcessStartInfo(Executable, Arguments) {
            WorkingDirectory = WorkingDirectory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        using (var child = new System.Diagnostics.Process()) {
            child.StartInfo = start;
            child.OutputDataReceived += (sender, line) => { if (line.Data != null) Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "{0}", line.Data); };
            child.ErrorDataReceived += (sender, line) => { if (line.Data != null) Log.LogMessage(Microsoft.Build.Framework.MessageImportance.High, "{0}", line.Data); };
            if (!child.Start()) { Log.LogError("The frozen native executable could not start."); return false; }
            child.BeginOutputReadLine(); child.BeginErrorReadLine(); child.WaitForExit();
            if (child.ExitCode != 0) { Log.LogError("The frozen native executable exited with code {0}.", child.ExitCode); return false; }
        }
        """;
    private static string Escape(string value)
    {
        StringBuilder result = new();
        foreach (char character in value)
        {
            if (character is '%' or '$' or '@' or ';' or '?' or '*' or '\'' or '(' or ')') result.Append('%').Append(((int)character).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            else result.Append(character);
        }
        return result.ToString();
    }
    private static string WindowsQuote(string value)
    {
        if (value.Contains('\0') || value.Contains('\r') || value.Contains('\n')) throw new ArgumentException("Native MSBuild arguments cannot contain control characters.");
        StringBuilder result = new("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes).Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
