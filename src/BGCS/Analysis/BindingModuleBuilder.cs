using System.Collections.Generic;
using System.Linq;
using BGCS.Intermediate;

namespace BGCS.Analysis;

internal sealed class BindingModuleBuilder
{
    internal BindingModuleBuilder(
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

    public string name { get; }
    public string @namespace { get; }
    public string libraryName { get; }
    public string targetAbi { get; }
    public string platformDefaultCallingConvention { get; set; } = "Unknown";
    public BindingImportMode importMode { get; set; } = BindingImportMode.DllImport;
    public bool useCustomContext { get; set; }
    public string getLibraryNameFunctionName { get; set; } = "GetLibraryName";
    public string? getLibraryExtensionFunctionName { get; set; }
    public bool emitLibraryNameConstant { get; set; } = true;
    public bool generateMetadata { get; set; }
    public bool generatePlaceholderComments { get; set; }
    public bool generateSizeOfStructs { get; set; }
    public bool generateConstructorsForStructs { get; set; }
    public bool autoWrapCallbacks { get; set; }
    public bool wrapPointersAsHandle { get; set; }
    public bool generateAdditionalOverloads { get; set; }
    public bool nestGeneratedTypesInApi { get; set; }
    public List<string> varyingTypes { get; set; } = new List<string>();
    public List<string> usings { get; set; } = new List<string>();
    public List<BindingType> types { get; set; } = new List<BindingType>();
    public List<BindingExternalTypeContract> externalTypes { get; set; } = new List<BindingExternalTypeContract>();
    public List<BindingConstant> constants { get; set; } = new List<BindingConstant>();
    public List<BindingDelegate> delegates { get; set; } = new List<BindingDelegate>();
    public List<BindingFunctionBuilder> functions { get; set; } = new List<BindingFunctionBuilder>();
    public List<BindingFunctionTableEntry> functionTableEntries { get; set; } = new List<BindingFunctionTableEntry>();
    public List<string> diagnostics { get; set; } = new List<string>();
    public List<BindingDiagnostic> structuredDiagnostics { get; set; } = [];

    internal BindingModule Freeze()
    {
        return new(this.name, this.@namespace, this.libraryName, this.targetAbi)
        {
            platformDefaultCallingConvention = this.platformDefaultCallingConvention,
            importMode = this.importMode,
            useCustomContext = this.useCustomContext,
            getLibraryNameFunctionName = this.getLibraryNameFunctionName,
            getLibraryExtensionFunctionName = this.getLibraryExtensionFunctionName,
            emitLibraryNameConstant = this.emitLibraryNameConstant,
            generateMetadata = this.generateMetadata,
            generatePlaceholderComments = this.generatePlaceholderComments,
            generateSizeOfStructs = this.generateSizeOfStructs,
            generateConstructorsForStructs = this.generateConstructorsForStructs,
            autoWrapCallbacks = this.autoWrapCallbacks,
            wrapPointersAsHandle = this.wrapPointersAsHandle,
            generateAdditionalOverloads = this.generateAdditionalOverloads,
            nestGeneratedTypesInApi = this.nestGeneratedTypesInApi,
            varyingTypes = this.varyingTypes,
            usings = this.usings,
            types = this.types,
            externalTypes = this.externalTypes,
            constants = this.constants,
            delegates = this.delegates,
            functions = this.functions.Select(static function => function.Freeze()).ToArray(),
            functionTableEntries = this.functionTableEntries,
            diagnostics = this.diagnostics,
            structuredDiagnostics = this.structuredDiagnostics
        };
    }

    internal static BindingModuleBuilder From(BindingModule model)
    {
        return new(model.name, model.@namespace, model.libraryName, model.targetAbi)
        {
            platformDefaultCallingConvention = model.platformDefaultCallingConvention,
            importMode = model.importMode,
            useCustomContext = model.useCustomContext,
            getLibraryNameFunctionName = model.getLibraryNameFunctionName,
            getLibraryExtensionFunctionName = model.getLibraryExtensionFunctionName,
            emitLibraryNameConstant = model.emitLibraryNameConstant,
            generateMetadata = model.generateMetadata,
            generatePlaceholderComments = model.generatePlaceholderComments,
            generateSizeOfStructs = model.generateSizeOfStructs,
            generateConstructorsForStructs = model.generateConstructorsForStructs,
            autoWrapCallbacks = model.autoWrapCallbacks,
            wrapPointersAsHandle = model.wrapPointersAsHandle,
            generateAdditionalOverloads = model.generateAdditionalOverloads,
            nestGeneratedTypesInApi = model.nestGeneratedTypesInApi,
            varyingTypes = model.varyingTypes.ToList(),
            usings = model.usings.ToList(),
            types = model.types.ToList(),
            externalTypes = model.externalTypes.ToList(),
            constants = model.constants.ToList(),
            delegates = model.delegates.ToList(),
            functions = model.functions.Select(static function => BindingFunctionBuilder.From(function)).ToList(),
            functionTableEntries = model.functionTableEntries.ToList(),
            diagnostics = model.diagnostics.ToList(),
            structuredDiagnostics = model.structuredDiagnostics.ToList()
        };
    }
}
