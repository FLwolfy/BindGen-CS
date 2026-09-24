// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BGCS.CppAst.Targeting;

namespace BGCS.CppAst.Parsing;
/// <summary>
/// Defines the options used by the <see cref="CppParser"/>
/// </summary>
public class CppParserOptions
{
    private List<string> targetSystemIncludeFolders = [];
    private List<string> targetAdditionalArguments = [];

    /// <summary>
    /// Default constructor.
    /// </summary>
    public CppParserOptions()
    {
        ParserKind = CppParserKind.Cpp;
        SystemIncludeFolders = [];
        IncludeFolders = [];

        //Add a default macro here for CppAst.Net
        Defines = [ 
            "__cppast_run__",                                     //Help us for identify the CppAst.Net handler
            @"__cppast_impl(...)=__attribute__((annotate(#__VA_ARGS__)))",          //Help us for use annotate attribute convenience
            @"__cppast(...)=__cppast_impl(__VA_ARGS__)",                         //Add a macro wrapper here, so the argument with macro can be handle right for compiler.
        ];
        AdditionalArguments =
        [
            "-Wno-pragma-once-outside-header"
        ];
        AutoSquashTypedef = true;
        ParseMacros = false;
        ParseComments = true;
        ParseSystemIncludes = true;
        ParseTokenAttributes = false;
        ParseCommentAttribute = false;

        // Default triple targets
        TargetCpu = nint.Size == 8 ? CppTargetCpu.X86_64 : CppTargetCpu.X86;
        TargetCpuSub = string.Empty;
        TargetVendor = "pc";
        TargetSystem = "windows";
        TargetAbi = "";
        ConfigureForTarget(CppTarget.Resolve(), discoverHostToolchain: false);
    }

    /// <summary>
    /// List of the include folders.
    /// </summary>
    public List<string> IncludeFolders { get; private set; }

    /// <summary>
    /// List of the system include folders.
    /// </summary>
    public List<string> SystemIncludeFolders { get; private set; }

    /// <summary>
    /// List of the defines.
    /// </summary>
    public List<string> Defines { get; private set; }

    /// <summary>
    /// List of the additional arguments passed directly to the C++ Clang compiler.
    /// </summary>
    public List<string> AdditionalArguments { get; private set; }

    /// <summary>
    /// Gets or sets the parser kind. Default is <see cref="CppParserKind.Cpp"/>. This is used to select the parser to use.
    /// </summary>
    public CppParserKind ParserKind { get; set; } = CppParserKind.Cpp;
    
    /// <summary>
    /// Gets or sets a boolean indicating whether to parser non-Doxygen comments in addition to Doxygen comments. Default is <c>true</c>
    /// </summary>
    public bool ParseComments { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating whether to parse macros. Default is <c>false</c>.
    /// </summary>
    public bool ParseMacros { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating whether un-named enum/struct referenced by a typedef will be renamed directly to the typedef name. Default is <c>true</c>
    /// </summary>
    public bool AutoSquashTypedef { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating whether to parse System Include headers. Default is <c>true</c>
    /// </summary>
    public bool ParseSystemIncludes { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating whether to parse meta attributes. Default is <c>false</c>
    /// </summary>
    public bool ParseTokenAttributes { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating whether to parse comment attributes. Default is <c>false</c>
    /// </summary>
    public bool ParseCommentAttribute { get; set; }

    /// <summary>
    /// Sets <see cref="ParseMacros"/> to <c>true</c> and return this instance.
    /// </summary>
    /// <returns>This instance</returns>
    public CppParserOptions EnableMacros()
    {
        ParseMacros = true;
        return this;
    }

    /// <summary>
    /// Cpu Clang target. Default is <see cref="CppTargetCpu.X86"/>
    /// </summary>
    public CppTargetCpu TargetCpu { get; set; }

    /// <summary>
    /// Cpu sub Clang target. Default is ""
    /// </summary>
    public string TargetCpuSub { get; set; }

    /// <summary>
    /// Vendor Clang target. Default is "pc"
    /// </summary>
    public string TargetVendor { get; set; }

    /// <summary>
    /// System Clang target. Default is "windows"
    /// </summary>
    public string TargetSystem { get; set; }

    /// <summary>
    /// Abi Clang target. Default is ""
    /// </summary>
    public string TargetAbi { get; set; }

    /// <summary>
    /// Gets or sets an explicit Clang target triple. When set, it takes precedence over the component fields.
    /// </summary>
    public string? TargetTriple { get; set; }

    /// <summary>
    /// Gets or sets a C/C++ pre-header included before the files/text to parse
    /// </summary>
    public string? PreHeaderText { get; set; }

    /// <summary>
    /// Gets or sets a C/C++ post-header included after the files/text to parse
    /// </summary>
    public string? PostHeaderText { get; set; }

    /// <summary>
    /// Clone this instance.
    /// </summary>
    /// <returns>Return a copy of this options.</returns>
    public virtual CppParserOptions Clone()
    {
        var newOptions = (CppParserOptions)MemberwiseClone();

        // Copy lists
        newOptions.IncludeFolders = new List<string>(IncludeFolders);
        newOptions.SystemIncludeFolders = new List<string>(SystemIncludeFolders);
        newOptions.Defines = new List<string>(Defines);
        newOptions.AdditionalArguments = new List<string>(AdditionalArguments);
        newOptions.targetSystemIncludeFolders = new List<string>(targetSystemIncludeFolders);
        newOptions.targetAdditionalArguments = new List<string>(targetAdditionalArguments);

        return newOptions;
    }

    /// <summary>
    /// Configure this instance with Windows and MSVC.
    /// </summary>
    /// <returns>This instance</returns>
    public CppParserOptions ConfigureForWindowsMsvc(CppTargetCpu targetCpu = CppTargetCpu.X86, CppVisualStudioVersion vsVersion = CppVisualStudioVersion.VS2022)
    {
        ClearTargetConfiguration();
        // 1920
        var highVersion = (int)vsVersion / 100;  // => 19
        var lowVersion = (int)vsVersion % 100;   // => 20

        var versionAsString = $"{highVersion}.{lowVersion}";

        TargetCpu = targetCpu;
        TargetCpuSub = string.Empty;
        TargetVendor = "pc";
        TargetSystem = "windows";
        TargetAbi = $"msvc{versionAsString}";
        TargetTriple = targetCpu switch
        {
            CppTargetCpu.X86 => "i686-pc-windows-msvc",
            CppTargetCpu.X86_64 => "x86_64-pc-windows-msvc",
            CppTargetCpu.ARM => "armv7-pc-windows-msvc",
            CppTargetCpu.ARM64 => "aarch64-pc-windows-msvc",
            _ => throw new ArgumentOutOfRangeException(nameof(targetCpu), targetCpu, null)
        };

        // See https://docs.microsoft.com/en-us/cpp/preprocessor/predefined-macros?view=vs-2019

        Defines.Add($"_MSC_VER={(int)vsVersion}");
        Defines.Add("_WIN32=1");

        switch (targetCpu)
        {
            case CppTargetCpu.X86:
                Defines.Add("_M_IX86=600");
                break;
            case CppTargetCpu.X86_64:
                Defines.Add("_M_AMD64=100");
                Defines.Add("_M_X64=100");
                Defines.Add("_WIN64=1");
                break;
            case CppTargetCpu.ARM:
                Defines.Add("_M_ARM=7");
                break;
            case CppTargetCpu.ARM64:
                Defines.Add("_M_ARM64=1");
                Defines.Add("_WIN64=1");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(targetCpu), targetCpu, null);
        }

        AdditionalArguments.Add("-fms-extensions");
        AdditionalArguments.Add("-fms-compatibility");
        AdditionalArguments.Add($"-fms-compatibility-version={versionAsString}");
        if (OperatingSystem.IsWindows())
        {
            foreach (string include in (Environment.GetEnvironmentVariable("INCLUDE") ?? string.Empty)
                .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (System.IO.Directory.Exists(include))
                    AddTargetSystemInclude(include);
            }
        }
        return this;
    }

    /// <summary>
    /// Configures this instance for a resolved cross-platform native target.
    /// </summary>
    /// <param name="target">Resolved target platform, architecture, ABI, and Clang triple.</param>
    /// <param name="sysRoot">Optional target SDK or sysroot.</param>
    /// <param name="compilerPath">Optional compiler driver used to discover host system headers.</param>
    /// <param name="discoverHostToolchain">Whether to discover SDK and system include paths when the target matches the host.</param>
    /// <returns>This instance.</returns>
    public CppParserOptions ConfigureForTarget(
        CppTarget target,
        string? sysRoot = null,
        string? compilerPath = null,
        bool discoverHostToolchain = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Platform == CppTargetPlatform.Windows)
        {
            ConfigureForWindowsMsvc(target.Cpu);
        }
        else
        {
            ClearTargetConfiguration();
            TargetCpu = target.Cpu;
            TargetCpuSub = string.Empty;
            TargetVendor = target.Platform is CppTargetPlatform.MacOS or CppTargetPlatform.IOS ? "apple" : "unknown";
            TargetSystem = target.Platform switch
            {
                CppTargetPlatform.MacOS => "darwin",
                CppTargetPlatform.IOS => "ios",
                CppTargetPlatform.Android => "linux",
                CppTargetPlatform.FreeBSD => "freebsd",
                _ => "linux"
            };
            TargetAbi = target.Abi switch
            {
                CppTargetAbi.Gnu => "gnu",
                CppTargetAbi.Musl => "musl",
                CppTargetAbi.Android => "android",
                _ => string.Empty
            };
        }
        TargetTriple = target.Triple;

        string? effectiveSysRoot = discoverHostToolchain && string.IsNullOrWhiteSpace(sysRoot) && target.Platform == CppTargetPlatform.MacOS
            ? CppToolchainDiscovery.FindMacOsSdkRoot()
            : sysRoot;
        if (!string.IsNullOrWhiteSpace(effectiveSysRoot))
        {
            string fullSysRoot = System.IO.Path.GetFullPath(effectiveSysRoot);
            AddTargetArgument("-isysroot");
            AddTargetArgument(fullSysRoot);
            if (ParserKind == CppParserKind.Cpp)
            {
                string libcxx = System.IO.Path.Combine(fullSysRoot, "usr", "include", "c++", "v1");
                if (System.IO.Directory.Exists(libcxx))
                    AddTargetSystemInclude(libcxx);
            }
        }

        CppTarget host = CppTarget.Resolve();
        if (discoverHostToolchain && target.Platform == host.Platform && target.Architecture == host.Architecture)
        {
            foreach (string include in CppToolchainDiscovery.DiscoverSystemIncludeFolders(ParserKind, compilerPath))
            {
                AddTargetSystemInclude(include);
            }
        }
        return this;
    }

    private void ClearTargetConfiguration()
    {
        foreach (string include in targetSystemIncludeFolders)
            SystemIncludeFolders.Remove(include);
        targetSystemIncludeFolders.Clear();
        foreach (string argument in targetAdditionalArguments)
        {
            int index = AdditionalArguments.LastIndexOf(argument);
            if (index >= 0)
                AdditionalArguments.RemoveAt(index);
        }
        targetAdditionalArguments.Clear();
        Defines.RemoveAll(define =>
            define.StartsWith("_MSC_VER=", StringComparison.Ordinal) ||
            define is "_WIN32=1" or "_WIN64=1" or "_M_IX86=600" or "_M_AMD64=100" or "_M_X64=100" or "_M_ARM=7" or "_M_ARM64=1");
        AdditionalArguments.RemoveAll(argument =>
            argument is "-fms-extensions" or "-fms-compatibility" ||
            argument.StartsWith("-fms-compatibility-version=", StringComparison.Ordinal));
    }

    private void AddTargetSystemInclude(string include)
    {
        if (!SystemIncludeFolders.Contains(include))
        {
            SystemIncludeFolders.Add(include);
            targetSystemIncludeFolders.Add(include);
        }
    }

    private void AddTargetArgument(string argument)
    {
        AdditionalArguments.Add(argument);
        targetAdditionalArguments.Add(argument);
    }
}
