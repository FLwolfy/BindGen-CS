namespace BGCS.Configuration;

using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;

/// <summary>
/// Applies named, versioned groups of generator defaults before validation.
/// </summary>
public sealed class PresetResolver
{
    private readonly Dictionary<string, Action<CsCodeGeneratorConfig>> presets =
        new(StringComparer.OrdinalIgnoreCase);

    public PresetResolver()
    {
        Register("host-c", config =>
        {
            config.ParserKind = CppParserKind.C;
            config.TargetPlatform = CppTargetPlatform.Host;
            config.TargetArchitecture = CppTargetArchitecture.Host;
            config.TargetAbi = CppTargetAbi.Default;
            config.ParseSystemIncludes = false;
        });
        Register("host-cpp", config =>
        {
            config.ParserKind = CppParserKind.Cpp;
            config.TargetPlatform = CppTargetPlatform.Host;
            config.TargetArchitecture = CppTargetArchitecture.Host;
            config.TargetAbi = CppTargetAbi.Default;
            config.ParseSystemIncludes = false;
        });
        RegisterTargetPreset("windows-c", CppParserKind.C, CppTargetPlatform.Windows, CppTargetArchitecture.X64, CppTargetAbi.Msvc);
        RegisterTargetPreset("windows-cpp", CppParserKind.Cpp, CppTargetPlatform.Windows, CppTargetArchitecture.X64, CppTargetAbi.Msvc);
        RegisterTargetPreset("linux-c", CppParserKind.C, CppTargetPlatform.Linux, CppTargetArchitecture.X64, CppTargetAbi.Gnu);
        RegisterTargetPreset("linux-cpp", CppParserKind.Cpp, CppTargetPlatform.Linux, CppTargetArchitecture.X64, CppTargetAbi.Gnu);
        RegisterTargetPreset("macos-c", CppParserKind.C, CppTargetPlatform.MacOS, CppTargetArchitecture.Arm64, CppTargetAbi.Darwin);
        RegisterTargetPreset("macos-cpp", CppParserKind.Cpp, CppTargetPlatform.MacOS, CppTargetArchitecture.Arm64, CppTargetAbi.Darwin);
        Register("function-table", config =>
        {
            config.ImportType = ImportType.FunctionTable;
            config.UseCustomContext = true;
            config.WrapPointersAsHandle = true;
            config.MergeGeneratedFilesToSingleFile = true;
        });
        Register("c-library", config =>
        {
            config.ParserKind = CppParserKind.C;
            config.AutoSquashTypedef = false;
            config.ParseSystemIncludes = false;
            config.ParseMacros = false;
            config.ParseComments = false;
            config.ImportType = ImportType.DllImport;
            config.GenerateExtensions = false;
            config.OneFilePerType = false;
            config.MergeGeneratedFilesToSingleFile = true;
            config.SingleFileOutputName = "Bindings.cs";
            config.GenerateRuntimeSource = false;
        });
        Register("opaque-callbacks", config =>
        {
            config.DelegatesAsVoidPointer = true;
        });
    }

    public static PresetResolver Default { get; } = new();

    public IReadOnlyCollection<string> Names => presets.Keys;

    public void Register(string name, Action<CsCodeGeneratorConfig> apply)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A preset name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(apply);
        if (!presets.TryAdd(name, apply))
            throw new InvalidOperationException($"Preset '{name}' is already registered.");
    }

    public void Apply(CsCodeGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Preset))
            return;
        string[] names = config.Preset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string name in names)
        {
            if (!presets.TryGetValue(name, out Action<CsCodeGeneratorConfig>? apply))
                throw new InvalidOperationException($"Unknown BindGen-CS preset '{name}'. Available presets: {string.Join(", ", Names.OrderBy(value => value))}.");
            apply(config);
        }
    }

    private void RegisterTargetPreset(
        string name,
        CppParserKind parserKind,
        CppTargetPlatform platform,
        CppTargetArchitecture architecture,
        CppTargetAbi abi)
    {
        Register(name, config =>
        {
            config.ParserKind = parserKind;
            config.TargetPlatform = platform;
            config.TargetArchitecture = architecture;
            config.TargetAbi = abi;
            config.ParseSystemIncludes = false;
        });
    }
}
