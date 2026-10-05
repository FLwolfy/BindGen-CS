using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Configuration;

using BGCS.CppAst.Parsing;

/// <summary>
/// Applies named groups of generator defaults before validation.
/// </summary>
public sealed class PresetResolver
{
    private readonly Dictionary<string, Action<CsCodeGeneratorConfig>> m_presets = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Creates a preset catalog populated with the built-in target and import defaults.
    /// </summary>
    public PresetResolver()
    {
        Register("host-c", config =>
        {
            config.parserKind = CppParserKind.C;
            config.targetId = "host";
            config.parseSystemIncludes = false;
        });
        Register("host-cpp", config =>
        {
            config.parserKind = CppParserKind.Cpp;
            config.targetId = "host";
            config.parseSystemIncludes = false;
        });
        RegisterTargetPreset("windows-c", CppParserKind.C, "windows", "x64", "msvc");
        RegisterTargetPreset("windows-cpp", CppParserKind.Cpp, "windows", "x64", "msvc");
        RegisterTargetPreset("linux-c", CppParserKind.C, "linux", "x64", "gnu");
        RegisterTargetPreset("linux-cpp", CppParserKind.Cpp, "linux", "x64", "gnu");
        RegisterTargetPreset("macos-c", CppParserKind.C, "macos", "arm64", "darwin");
        RegisterTargetPreset("macos-cpp", CppParserKind.Cpp, "macos", "arm64", "darwin");
        RegisterTargetPreset("emscripten-c", CppParserKind.C, "emscripten", "wasm32", "emscripten");
        RegisterTargetPreset("emscripten-cpp", CppParserKind.Cpp, "emscripten", "wasm32", "emscripten");
        Register("function-table", config =>
        {
            config.importType = ImportType.FunctionTable;
            config.useCustomContext = true;
            config.wrapPointersAsHandle = true;
            config.mergeGeneratedFilesToSingleFile = true;
        });
        Register("c-library", config =>
        {
            config.parserKind = CppParserKind.C;
            config.autoSquashTypedef = false;
            config.parseSystemIncludes = false;
            config.parseMacros = false;
            config.parseComments = false;
            config.importType = ImportType.DllImport;
            config.generateExtensions = false;
            config.oneFilePerType = false;
            config.mergeGeneratedFilesToSingleFile = true;
            config.singleFileOutputName = "Bindings.cs";
            config.generateRuntimeSource = false;
        });
        Register("opaque-callbacks", config =>
        {
            config.delegatesAsVoidPointer = true;
        });
    }

    /// <summary>
    /// Gets the shared built-in catalog. Applications extending presets should create their own catalog.
    /// </summary>
    public static PresetResolver @default { get; } = new();
    /// <summary>
    /// Gets the currently registered names using case-insensitive preset identity.
    /// </summary>
    public IReadOnlyCollection<string> names => this.m_presets.Keys;

    /// <summary>
    /// Registers a named configuration transform, applied in the order declared by configuration.
    /// </summary>
    /// <param name="name">Nonempty preset name, unique without regard to case.</param>
    /// <param name="apply">Transform retained by the catalog and invoked during configuration loading.</param>
    /// <exception cref="ArgumentException">The name is empty.</exception>
    /// <exception cref="ArgumentNullException">The transform is null.</exception>
    /// <exception cref="InvalidOperationException">The name is already registered.</exception>
    public void Register(
        string name,
        Action<CsCodeGeneratorConfig> apply
    ) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A preset name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(apply);
        if (!this.m_presets.TryAdd(name, apply))
            throw new InvalidOperationException($"Preset '{name}' is already registered.");
    }

    /// <summary>
    /// Applies the comma-separated presets named by the supplied configuration.
    /// </summary>
    /// <param name="config">Configuration receiving the transforms; an empty preset selection changes nothing.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="InvalidOperationException">A requested preset is not registered.</exception>
    public void Apply(CsCodeGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.preset))
            return;
        string[] names = config.preset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string name in names)
        {
            if (!this.m_presets.TryGetValue(name, out Action<CsCodeGeneratorConfig>? apply))
                throw new InvalidOperationException($"Unknown BindGen-CS preset '{name}'. Available presets: {string.Join(", ", this.names.OrderBy(value => value))}.");
            apply(config);
        }
    }

    private void RegisterTargetPreset(
        string name,
        CppParserKind parserKind,
        string platform,
        string architecture,
        string abi
    ) {
        Register(name, config =>
        {
            config.parserKind = parserKind;
            config.targetId = string.Join("-", platform, architecture, abi);
            config.parseSystemIncludes = false;
        });
    }
}
