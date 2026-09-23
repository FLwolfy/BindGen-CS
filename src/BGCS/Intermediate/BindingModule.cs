namespace BGCS.Intermediate;

/// <summary>
/// Contains the complete analyzed binding model consumed by all emitters.
/// </summary>
public sealed class BindingModule
{
    public BindingModule(string name, string @namespace, string libraryName, string targetAbi)
    {
        Name = name;
        Namespace = @namespace;
        LibraryName = libraryName;
        TargetAbi = targetAbi;
    }

    public string Name { get; }
    public string Namespace { get; }
    public string LibraryName { get; }
    public string TargetAbi { get; }
    public BindingImportMode ImportMode { get; init; } = BindingImportMode.DllImport;
    public bool UseCustomContext { get; init; }
    public string GetLibraryNameFunctionName { get; init; } = "GetLibraryName";
    public string? GetLibraryExtensionFunctionName { get; init; }
    public bool EmitLibraryNameConstant { get; init; } = true;
    public bool GenerateMetadata { get; init; }
    public bool GeneratePlaceholderComments { get; init; }
    public bool GenerateSizeOfStructs { get; init; }
    public bool GenerateConstructorsForStructs { get; init; }
    public bool AutoWrapCallbacks { get; init; }
    public bool WrapPointersAsHandle { get; init; }
    public bool GenerateAdditionalOverloads { get; init; }
    public bool NestGeneratedTypesInApi { get; init; }
    public IList<string> VaryingTypes { get; } = new List<string>();
    public IList<string> Usings { get; } = new List<string>();
    public IList<BindingType> Types { get; } = new List<BindingType>();
    public IList<BindingConstant> Constants { get; } = new List<BindingConstant>();
    public IList<BindingDelegate> Delegates { get; } = new List<BindingDelegate>();
    public IList<BindingFunction> Functions { get; } = new List<BindingFunction>();
    public IList<BindingFunctionTableEntry> FunctionTableEntries { get; } = new List<BindingFunctionTableEntry>();
    public IList<string> Diagnostics { get; } = new List<string>();
    public IList<BindingDiagnostic> StructuredDiagnostics { get; } = new List<BindingDiagnostic>();
}
