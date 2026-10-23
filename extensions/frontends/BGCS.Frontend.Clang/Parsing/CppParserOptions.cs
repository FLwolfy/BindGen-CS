using BGCS.Core.Inputs;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using BGCS.Frontend.Clang.Interop;
using BGCS.Core.Targets;
using BGCS.Core.Hosting;

namespace BGCS.Frontend.Clang.Parsing;

/// <summary>
/// Defines the options used by the <see cref = "CppParser"/>
/// </summary>
internal class CppParserOptions
{
    internal string resourceDirectory { get; init; } = string.Empty;
    internal IHostPathService? hostPaths { get; init; }
    private List<string> m_targetSystemIncludeFolders = [];
    private List<string> m_targetAdditionalArguments = [];
    private List<string> m_targetDefines = [];
    private HashSet<string> m_afterBuiltinIncludes = [];
    private HashSet<string> m_frameworkIncludes = [];
    /// <summary>Gets immutable physical-file to logical-source provenance mappings for generated inputs.</summary>
    public ImmutableDictionary<string, string> sourcePathMappings { get; init; } = ImmutableDictionary<string, string>.Empty;
    /// <summary>
    /// Default constructor.
    /// </summary>
    public CppParserOptions()
    {
        this.parserKind = SourceLanguage.Cpp;
        this.systemIncludeFolders = [];
        this.includeFolders = [];
        //Add a default macro here for CppAst.Net
        this.defines = ["__cppast_run__", //Help us for identify the CppAst.Net handler
 @"__cppast_impl(...)=__attribute__((annotate(#__VA_ARGS__)))", //Help us for use annotate attribute convenience
 @"__cppast(...)=__cppast_impl(__VA_ARGS__)", //Add a macro wrapper here, so the argument with macro can be handle right for compiler.
 ];
        this.additionalArguments = ["-Wno-pragma-once-outside-header"];
        this.autoSquashTypedef = true;
        this.parseMacros = false;
        this.parseComments = true;
        this.parseSystemIncludes = true;
        this.parseTokenAttributes = false;
        this.parseCommentAttribute = false;
    }

    /// <summary>
    /// List of the include folders.
    /// </summary>
    public List<string> includeFolders { get; private set; }
    /// <summary>
    /// List of the system include folders.
    /// </summary>
    public List<string> systemIncludeFolders { get; private set; }
    /// <summary>
    /// List of the defines.
    /// </summary>
    public List<string> defines { get; private set; }
    /// <summary>
    /// List of the additional arguments passed directly to the C++ Clang compiler.
    /// </summary>
    public List<string> additionalArguments { get; private set; }
    /// <summary>
    /// Gets or sets the parser kind. Default is <see cref = "SourceLanguage.Cpp"/>. This is used to select the parser to use.
    /// </summary>
    public SourceLanguage parserKind { get; set; } = SourceLanguage.Cpp;
    /// <summary>
    /// Gets or sets a boolean indicating whether to parser non-Doxygen comments in addition to Doxygen comments. Default is <c>true</c>
    /// </summary>
    public bool parseComments { get; set; }
    /// <summary>Gets or sets whether C++ bodies are retained when compiler-established definition facts are required.</summary>
    public bool retainFunctionBodies { get; set; }
    /// <summary>
    /// Gets or sets a boolean indicating whether to parse macros. Default is <c>false</c>.
    /// </summary>
    public bool parseMacros { get; set; }
    /// <summary>
    /// Gets or sets a boolean indicating whether un-named enum/struct referenced by a typedef will be renamed directly to the typedef name. Default is <c>true</c>
    /// </summary>
    public bool autoSquashTypedef { get; set; }
    /// <summary>
    /// Gets or sets a boolean indicating whether to parse System Include headers. Default is <c>true</c>
    /// </summary>
    public bool parseSystemIncludes { get; set; }
    /// <summary>Gets or sets whether detached diagnostic positions honor compiler-produced line directives; token offsets remain physical.</summary>
    public bool usePresumedSourceLocations { get; set; }
    /// <summary>
    /// Gets or sets a boolean indicating whether to parse meta attributes. Default is <c>false</c>
    /// </summary>
    public bool parseTokenAttributes { get; set; }
    /// <summary>
    /// Gets or sets a boolean indicating whether to parse comment attributes. Default is <c>false</c>
    /// </summary>
    public bool parseCommentAttribute { get; set; }

    /// <summary>
    /// Sets <see cref = "parseMacros"/> to <c>true</c> and return this instance.
    /// </summary>
    /// <returns>This instance</returns>
    public CppParserOptions EnableMacros()
    {
        this.parseMacros = true;
        return this;
    }

    /// <summary>
    /// Gets or sets an explicit Clang target triple. When set, it takes precedence over the component fields.
    /// </summary>
    public string? targetTriple { get; set; }
    /// <summary>
    /// Gets or sets a C/C++ pre-header included before the files/text to parse
    /// </summary>
    public string? preHeaderText { get; set; }
    /// <summary>
    /// Gets or sets a C/C++ post-header included after the files/text to parse
    /// </summary>
    public string? postHeaderText { get; set; }

    /// <summary>
    /// Clone this instance.
    /// </summary>
    /// <returns>Return a copy of this options.</returns>
    public virtual CppParserOptions Clone()
    {
        var newOptions = (CppParserOptions)MemberwiseClone();
        // Copy lists
        newOptions.includeFolders = new List<string>(this.includeFolders);
        newOptions.systemIncludeFolders = new List<string>(this.systemIncludeFolders);
        newOptions.defines = new List<string>(this.defines);
        newOptions.additionalArguments = new List<string>(this.additionalArguments);
        newOptions.m_targetSystemIncludeFolders = new List<string>(this.m_targetSystemIncludeFolders);
        newOptions.m_targetAdditionalArguments = new List<string>(this.m_targetAdditionalArguments);
        newOptions.m_targetDefines = new List<string>(m_targetDefines);
        newOptions.m_afterBuiltinIncludes = new HashSet<string>(m_afterBuiltinIncludes);
        newOptions.m_frameworkIncludes = new HashSet<string>(m_frameworkIncludes);
        return newOptions;
    }

    /// <summary>
    /// Configures this instance for a resolved cross-platform native target.
    /// </summary>
    /// <param name = "target">Resolved target platform, architecture, ABI, and Clang triple.</param>
    /// <param name = "headerSearchPaths">Resolved driver search order supplied by the caller; no toolchain discovery runs in the frontend.</param>
    /// <returns>This instance.</returns>
    public CppParserOptions ConfigureForTarget(
        NativeTargetDescriptor target,
        IReadOnlyList<HeaderSearchPath>? headerSearchPaths = null
    ) {
        ArgumentNullException.ThrowIfNull(target);
        ClearTargetConfiguration();
        this.targetTriple = target.triple;
        // Every OS/SDK root must be supplied and snapshotted by the caller.
        // Keep only the independently locked compiler builtin resource search.
        AddTargetArgument("-nostdlibinc");
        AddTargetArgument("-nostdinc++");
        foreach (string definition in target.toolchain.defines)
        {
            if (!this.defines.Contains(definition))
            {
                this.defines.Add(definition);
                m_targetDefines.Add(definition);
            }
        }

        foreach (string argument in target.toolchain.arguments)
            AddTargetArgument(argument);
        // C++ wrappers use include_next to reach C headers, so their search roots must come first.
        if (this.parserKind == SourceLanguage.Cpp)
        {
            foreach (string include in target.toolchain.cxxSystemIncludeFolders)
                AddTargetSystemInclude(include);
        }
        string? effectiveSysRoot = target.toolchain.sysRoot;
        IReadOnlyList<HeaderSearchPath> discoveredIncludes = headerSearchPaths ?? [];

        // Keep the driver's builtin position: C++ wrappers -> parser builtins -> SDK C headers.
        int builtinIndex = -1;
        for (int index = 0; index < discoveredIncludes.Count; index++)
        {
            if (discoveredIncludes[index].isBuiltin)
            {
                builtinIndex = index;
                break;
            }
        }
        for (int index = 0; index < builtinIndex; index++)
            AddTargetSystemInclude(discoveredIncludes[index].path, isFramework: discoveredIncludes[index].isFramework);
        foreach (string include in target.toolchain.systemIncludeFolders)
            AddTargetSystemInclude(include, afterBuiltin: true);
        for (int index = builtinIndex + 1; index < discoveredIncludes.Count; index++)
        {
            HeaderSearchPath entry = discoveredIncludes[index];
            if (!entry.isBuiltin)
                AddTargetSystemInclude(entry.path, afterBuiltin: builtinIndex >= 0, isFramework: entry.isFramework);
        }

        if (!string.IsNullOrWhiteSpace(effectiveSysRoot))
        {
            string fullSysRoot = System.IO.Path.GetFullPath(effectiveSysRoot);
            AddTargetArgument("--sysroot=" + fullSysRoot);
            // Clang distinguishes the driver/linker sysroot from the header-search sysroot.
            AddTargetArgument("-isysroot");
            AddTargetArgument(fullSysRoot);
        }
        return this;
    }

    private void ClearTargetConfiguration()
    {
        foreach (string include in this.m_targetSystemIncludeFolders)
            this.systemIncludeFolders.Remove(include);
        this.m_targetSystemIncludeFolders.Clear();
        m_afterBuiltinIncludes.Clear();
        m_frameworkIncludes.Clear();
        foreach (string argument in this.m_targetAdditionalArguments)
        {
            int index = this.additionalArguments.LastIndexOf(argument);
            if (index >= 0)
                this.additionalArguments.RemoveAt(index);
        }

        this.m_targetAdditionalArguments.Clear();
        foreach (string definition in m_targetDefines)
            this.defines.Remove(definition);
        m_targetDefines.Clear();
    }

    internal void AppendSystemIncludeArguments(List<string> arguments)
    {
        bool builtinAdded = false;
        foreach (string include in systemIncludeFolders)
        {
            if (!builtinAdded && m_afterBuiltinIncludes.Contains(include))
            {
                arguments.Add("-isystem" + Path.Combine(resourceDirectory, "include"));
                builtinAdded = true;
            }
            string option = m_frameworkIncludes.Contains(include) ? "-iframework" : "-isystem";
            arguments.Add(option + Path.GetFullPath(include));
        }
    }

    private void AddTargetSystemInclude(
        string include,
        bool afterBuiltin = false,
        bool isFramework = false
    ) {
        if (!this.systemIncludeFolders.Contains(include))
        {
            this.systemIncludeFolders.Add(include);
            this.m_targetSystemIncludeFolders.Add(include);
        }
        if (afterBuiltin)
            m_afterBuiltinIncludes.Add(include);
        if (isFramework)
            m_frameworkIncludes.Add(include);
    }

    private void AddTargetArgument(string argument)
    {
        this.additionalArguments.Add(argument);
        this.m_targetAdditionalArguments.Add(argument);
    }
}
