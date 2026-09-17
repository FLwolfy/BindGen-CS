namespace BGCS.Configuration;

using BGCS.CppAst.Parsing;

/// <summary>
/// Applies named, versioned groups of generator defaults before validation.
/// </summary>
public sealed class PresetResolver
{
    private readonly Dictionary<string, Action<CsCodeGeneratorConfig>> presets =
        new(StringComparer.OrdinalIgnoreCase);

    public PresetResolver()
    {
        Register("windows-c", config =>
        {
            config.ParserKind = CppParserKind.C;
            config.TargetArchitecture = WindowsTargetArchitecture.X64;
            config.ParseSystemIncludes = false;
        });
        Register("windows-cpp", config =>
        {
            config.ParserKind = CppParserKind.Cpp;
            config.TargetArchitecture = WindowsTargetArchitecture.X64;
            config.ParseSystemIncludes = false;
        });
        Register("inno-function-table", config =>
        {
            config.ImportType = ImportType.FunctionTable;
            config.UseCustomContext = true;
            config.WrapPointersAsHandle = true;
            config.MergeGeneratedFilesToSingleFile = true;
        });
        Register("bgfx", config =>
        {
            config.ParserKind = CppParserKind.C;
            config.TargetArchitecture = WindowsTargetArchitecture.X64;
            config.AutoSquashTypedef = false;
            config.ParseSystemIncludes = false;
            config.ParseMacros = false;
            config.ParseComments = false;
            config.DelegatesAsVoidPointer = true;
            config.ImportType = ImportType.DllImport;
            config.GenerateExtensions = false;
            config.OneFilePerType = false;
            config.MergeGeneratedFilesToSingleFile = true;
            config.SingleFileOutputName = "Bindings.cs";
            config.GenerateRuntimeSource = false;
        });
        Register("sdl3", config =>
        {
            config.ParserKind = CppParserKind.C;
            config.TargetArchitecture = WindowsTargetArchitecture.X64;
            config.AutoSquashTypedef = false;
            config.ParseSystemIncludes = false;
            config.ParseMacros = false;
            config.ParseComments = false;
            config.DelegatesAsVoidPointer = true;
            config.ImportType = ImportType.DllImport;
            config.GenerateExtensions = false;
            config.OneFilePerType = false;
            config.MergeGeneratedFilesToSingleFile = true;
            config.SingleFileOutputName = "Bindings.cs";
            config.GenerateRuntimeSource = false;
        });
        Register("cimguizmo", config =>
        {
            config.ParserKind = CppParserKind.C;
            config.TargetArchitecture = WindowsTargetArchitecture.X64;
            config.AutoSquashTypedef = false;
            config.ParseSystemIncludes = false;
            config.ParseMacros = false;
            config.ParseComments = false;
            config.DelegatesAsVoidPointer = true;
            config.ImportType = ImportType.DllImport;
            config.GenerateExtensions = false;
            config.OneFilePerType = false;
            config.MergeGeneratedFilesToSingleFile = true;
            config.SingleFileOutputName = "Bindings.cs";
            config.GenerateRuntimeSource = false;
            if (!config.Defines.Contains("CIMGUI_DEFINE_ENUMS_AND_STRUCTS", StringComparer.Ordinal))
                config.Defines.Add("CIMGUI_DEFINE_ENUMS_AND_STRUCTS");
        });
        Register("cimgui", config =>
        {
            config.ParserKind = CppParserKind.C;
            config.TargetArchitecture = WindowsTargetArchitecture.X64;
            config.AutoSquashTypedef = false;
            config.ParseSystemIncludes = false;
            config.ParseMacros = false;
            config.ParseComments = false;
            config.DelegatesAsVoidPointer = true;
            config.ImportType = ImportType.DllImport;
            config.GenerateExtensions = false;
            config.OneFilePerType = false;
            config.MergeGeneratedFilesToSingleFile = true;
            config.SingleFileOutputName = "Bindings.cs";
            config.GenerateRuntimeSource = false;
            if (!config.Defines.Contains("CIMGUI_DEFINE_ENUMS_AND_STRUCTS", StringComparer.Ordinal))
                config.Defines.Add("CIMGUI_DEFINE_ENUMS_AND_STRUCTS");
        });
        Register("miniaudio", config =>
        {
            config.ParserKind = CppParserKind.C;
            config.TargetArchitecture = WindowsTargetArchitecture.X64;
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
}
