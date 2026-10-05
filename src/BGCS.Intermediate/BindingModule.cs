using System.Collections.Generic;

namespace BGCS.Intermediate;

/// <summary>
/// Contains the complete analyzed binding model consumed by all emitters.
/// </summary>
public sealed record BindingModule
{
    private IReadOnlyList<string> m_varyingTypes = [];
    private IReadOnlyList<string> m_usings = [];
    private IReadOnlyList<BindingType> m_types = [];
    private IReadOnlyList<BindingExternalTypeContract> m_externalTypes = [];
    private IReadOnlyList<BindingConstant> m_constants = [];
    private IReadOnlyList<BindingDelegate> m_delegates = [];
    private IReadOnlyList<BindingFunction> m_functions = [];
    private IReadOnlyList<BindingFunctionTableEntry> m_functionTableEntries = [];
    private IReadOnlyList<string> m_diagnostics = [];
    private IReadOnlyList<BindingDiagnostic> m_structuredDiagnostics = [];
    /// <summary>
    /// Captures the resolved binding module identity before attaching immutable declarations.
    /// </summary>
    /// <param name="name">
    /// Managed API class name.
    /// </param>
    /// <param name="namespace">
    /// Managed declaration namespace.
    /// </param>
    /// <param name="libraryName">
    /// Native import module name.
    /// </param>
    /// <param name="targetAbi">
    /// Resolved native target identifier.
    /// </param>
    public BindingModule(
        string name,
        string @namespace,
        string libraryName,
        string targetAbi
    ) {
        this.name = name;
        this.@namespace = @namespace;
        this.libraryName = libraryName;
        this.targetAbi = targetAbi;
    }

    /// <summary>
    /// Gets the managed API class name selected during analysis.
    /// </summary>
    public string name { get; }
    /// <summary>
    /// Gets the managed namespace that owns generated declarations.
    /// </summary>
    public string @namespace { get; }
    /// <summary>
    /// Gets the native import module name passed to the runtime linker.
    /// </summary>
    public string libraryName { get; }
    /// <summary>
    /// Gets the resolved native target identifier used for ABI analysis.
    /// </summary>
    public string targetAbi { get; }
    /// <summary>
    /// Gets the unmanaged convention selected by the target runtime when an import has no convention modifier.
    /// Unknown targets retain explicit convention modifiers; analysis supplies this ABI fact for resolved targets.
    /// </summary>
    public string platformDefaultCallingConvention { get; init; } = "Unknown";
    /// <summary>
    /// Gets the mechanism used to resolve and invoke native symbols.
    /// </summary>
    public BindingImportMode importMode { get; init; } = BindingImportMode.DllImport;
    /// <summary>
    /// Gets whether the consumer supplies the native symbol context.
    /// </summary>
    public bool useCustomContext { get; init; }
    /// <summary>
    /// Gets the generated library name resolver method name.
    /// </summary>
    public string getLibraryNameFunctionName { get; init; } = "GetLibraryName";
    /// <summary>
    /// Gets the optional generated library extension resolver method name.
    /// </summary>
    public string? getLibraryExtensionFunctionName { get; init; }
    /// <summary>
    /// Gets whether generated imports include a native library name constant.
    /// </summary>
    public bool emitLibraryNameConstant { get; init; } = true;
    /// <summary>
    /// Gets whether generated declarations include native naming metadata.
    /// </summary>
    public bool generateMetadata { get; init; }
    /// <summary>
    /// Gets whether declarations without native documentation receive placeholder comments.
    /// </summary>
    public bool generatePlaceholderComments { get; init; }
    /// <summary>
    /// Gets whether generated value types expose their analyzed storage size.
    /// </summary>
    public bool generateSizeOfStructs { get; init; }
    /// <summary>
    /// Gets whether generated value types include field initialization constructors.
    /// </summary>
    public bool generateConstructorsForStructs { get; init; }
    /// <summary>
    /// Gets whether compatible native callbacks receive managed delegate wrappers.
    /// </summary>
    public bool autoWrapCallbacks { get; init; }
    /// <summary>
    /// Gets whether pointer-backed types receive generated handle wrappers.
    /// </summary>
    public bool wrapPointersAsHandle { get; init; }
    /// <summary>
    /// Gets whether analyzed friendly overloads are emitted.
    /// </summary>
    public bool generateAdditionalOverloads { get; init; }
    /// <summary>
    /// Gets whether generated types are nested in the managed API class.
    /// </summary>
    public bool nestGeneratedTypesInApi { get; init; }
    /// <summary>
    /// Gets a frozen copy of managed types used to specialize overloads.
    /// </summary>
    public IReadOnlyList<string> varyingTypes { get => m_varyingTypes; init => m_varyingTypes = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets a frozen copy of explicit namespaces required by emitted source.
    /// </summary>
    public IReadOnlyList<string> usings { get => m_usings; init => m_usings = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets frozen native type definitions with their analyzed target layouts.
    /// </summary>
    public IReadOnlyList<BindingType> types { get => m_types; init => m_types = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets frozen ABI evidence for managed carriers supplied by the consumer.
    /// </summary>
    public IReadOnlyList<BindingExternalTypeContract> externalTypes { get => m_externalTypes; init => m_externalTypes = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets frozen compile-time constants after preprocessing and naming.
    /// </summary>
    public IReadOnlyList<BindingConstant> constants { get => m_constants; init => m_constants = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets frozen callback signatures and their marshalling contracts.
    /// </summary>
    public IReadOnlyList<BindingDelegate> delegates { get => m_delegates; init => m_delegates = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets frozen callable declarations and their invocation contracts.
    /// </summary>
    public IReadOnlyList<BindingFunction> functions { get => m_functions; init => m_functions = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets deterministic native symbol slots for function-table imports.
    /// </summary>
    public IReadOnlyList<BindingFunctionTableEntry> functionTableEntries { get => m_functionTableEntries; init => m_functionTableEntries = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets frozen analysis messages that prevent a complete supported binding surface.
    /// </summary>
    public IReadOnlyList<string> diagnostics { get => m_diagnostics; init => m_diagnostics = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets frozen diagnostics with severity and stable identifiers.
    /// </summary>
    public IReadOnlyList<BindingDiagnostic> structuredDiagnostics { get => m_structuredDiagnostics; init => m_structuredDiagnostics = BindingCollection.Copy(value); }
}
