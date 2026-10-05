namespace BGCS.Configuration
{
    using System;

    /// <summary>
    /// Selects configuration groups copied or combined by CsCodeGeneratorConfig.Merge.
    /// </summary>
    [Flags]
    public enum MergeOptions : ulong
    {
        /// <summary>
        /// Keeps every setting in the receiving configuration unchanged.
        /// </summary>
        None = 0,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.enableExperimentalOptions"/> from the supplied configuration.
        /// </summary>
        EnableExperimentalOptions = 1L << 0,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generateSizeOfStructs"/> from the supplied configuration.
        /// </summary>
        GenerateSizeOfStructs = 1L << 1,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generateConstructorsForStructs"/> from the supplied configuration.
        /// </summary>
        GenerateConstructorsForStructs = 1L << 2,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.delegatesAsVoidPointer"/> from the supplied configuration.
        /// </summary>
        DelegatesAsVoidPointer = 1L << 3,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.wrapPointersAsHandle"/> from the supplied configuration.
        /// </summary>
        WrapPointersAsHandle = 1L << 4,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generatePlaceholderComments"/> from the supplied configuration.
        /// </summary>
        GeneratePlaceholderComments = 1L << 5,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.importType"/>, <see cref="CsCodeGeneratorConfig.emitLibraryNameConstant"/> from the supplied configuration.
        /// </summary>
        ImportType = 1L << 6,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generateMetadata"/> from the supplied configuration.
        /// </summary>
        GenerateMetadata = 1L << 7,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generateConstants"/> from the supplied configuration.
        /// </summary>
        GenerateConstants = 1L << 8,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generateEnums"/> from the supplied configuration.
        /// </summary>
        GenerateEnums = 1L << 9,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generateExtensions"/> from the supplied configuration.
        /// </summary>
        GenerateExtensions = 1L << 10,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generateFunctions"/> from the supplied configuration.
        /// </summary>
        GenerateFunctions = 1L << 11,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generateHandles"/> from the supplied configuration.
        /// </summary>
        GenerateHandles = 1L << 12,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generateTypes"/> from the supplied configuration.
        /// </summary>
        GenerateTypes = 1L << 13,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generateDelegates"/> from the supplied configuration.
        /// </summary>
        GenerateDelegates = 1L << 14,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.oneFilePerType"/>, <see cref="CsCodeGeneratorConfig.nestGeneratedTypesInApi"/> from the supplied configuration.
        /// </summary>
        OneFilePerType = 1L << 15,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.boolType"/>, <see cref="CsCodeGeneratorConfig.knownConstantNames"/>, <see cref="CsCodeGeneratorConfig.knownEnumValueNames"/>, <see cref="CsCodeGeneratorConfig.knownEnumPrefixes"/>, <see cref="CsCodeGeneratorConfig.knownExtensionPrefixes"/>, <see cref="CsCodeGeneratorConfig.knownExtensionNames"/>, <see cref="CsCodeGeneratorConfig.knownDefaultValueNames"/>, <see cref="CsCodeGeneratorConfig.knownConstructors"/>, <see cref="CsCodeGeneratorConfig.knownMemberFunctions"/>, <see cref="CsCodeGeneratorConfig.nameMappings"/>, <see cref="CsCodeGeneratorConfig.typeMappings"/>, <see cref="CsCodeGeneratorConfig.ignoredParts"/>, <see cref="CsCodeGeneratorConfig.keywords"/>, <see cref="CsCodeGeneratorConfig.ignoredFunctions"/>, <see cref="CsCodeGeneratorConfig.ignoredTypes"/>, <see cref="CsCodeGeneratorConfig.ignoredEnums"/>, <see cref="CsCodeGeneratorConfig.ignoredTypedefs"/>, <see cref="CsCodeGeneratorConfig.ignoredDelegates"/>, <see cref="CsCodeGeneratorConfig.ignoredConstants"/>, <see cref="CsCodeGeneratorConfig.allowedFunctions"/>, <see cref="CsCodeGeneratorConfig.allowedTypes"/>, <see cref="CsCodeGeneratorConfig.allowedEnums"/>, <see cref="CsCodeGeneratorConfig.allowedTypedefs"/>, <see cref="CsCodeGeneratorConfig.allowedDelegates"/>, <see cref="CsCodeGeneratorConfig.allowedConstants"/>, <see cref="CsCodeGeneratorConfig.constantMappings"/>, <see cref="CsCodeGeneratorConfig.enumMappings"/>, <see cref="CsCodeGeneratorConfig.functionPrefixes"/>, <see cref="CsCodeGeneratorConfig.functionMappings"/>, <see cref="CsCodeGeneratorConfig.handleMappings"/>, <see cref="CsCodeGeneratorConfig.classMappings"/>, <see cref="CsCodeGeneratorConfig.externalTypeContracts"/>, <see cref="CsCodeGeneratorConfig.delegateMappings"/>, <see cref="CsCodeGeneratorConfig.arrayMappings"/>, <see cref="CsCodeGeneratorConfig.usings"/>, <see cref="CsCodeGeneratorConfig.includeFolders"/>, <see cref="CsCodeGeneratorConfig.systemIncludeFolders"/>, <see cref="CsCodeGeneratorConfig.defines"/>, <see cref="CsCodeGeneratorConfig.additionalArguments"/> from the supplied configuration.
        /// </summary>
        BoolType = 1L << 16,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.knownConstantNames"/> from the supplied configuration.
        /// </summary>
        KnownConstantNames = 1L << 17,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.knownEnumValueNames"/> from the supplied configuration.
        /// </summary>
        KnownEnumValueNames = 1L << 18,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.knownEnumPrefixes"/> from the supplied configuration.
        /// </summary>
        KnownEnumPrefixes = 1L << 19,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.knownExtensionPrefixes"/> from the supplied configuration.
        /// </summary>
        KnownExtensionPrefixes = 1L << 20,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.knownExtensionNames"/> from the supplied configuration.
        /// </summary>
        KnownExtensionNames = 1L << 21,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.knownDefaultValueNames"/> from the supplied configuration.
        /// </summary>
        KnownDefaultValueNames = 1L << 22,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.knownConstructors"/> from the supplied configuration.
        /// </summary>
        KnownConstructors = 1L << 23,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.knownMemberFunctions"/> from the supplied configuration.
        /// </summary>
        KnownMemberFunctions = 1L << 24,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.ignoredParts"/> from the supplied configuration.
        /// </summary>
        IgnoredParts = 1L << 25,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.keywords"/> from the supplied configuration.
        /// </summary>
        Keywords = 1L << 26,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.ignoredFunctions"/> from the supplied configuration.
        /// </summary>
        IgnoredFunctions = 1L << 27,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.ignoredTypes"/> from the supplied configuration.
        /// </summary>
        IgnoredTypes = 1L << 28,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.ignoredEnums"/> from the supplied configuration.
        /// </summary>
        IgnoredEnums = 1L << 29,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.ignoredTypedefs"/> from the supplied configuration.
        /// </summary>
        IgnoredTypedefs = 1L << 30,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.ignoredDelegates"/> from the supplied configuration.
        /// </summary>
        IgnoredDelegates = 1L << 31,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.ignoredConstants"/> from the supplied configuration.
        /// </summary>
        IgnoredConstants = 1L << 32,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.allowedFunctions"/> from the supplied configuration.
        /// </summary>
        AllowedFunctions = 1L << 33,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.allowedTypes"/> from the supplied configuration.
        /// </summary>
        AllowedTypes = 1L << 34,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.allowedEnums"/> from the supplied configuration.
        /// </summary>
        AllowedEnums = 1L << 35,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.allowedTypedefs"/> from the supplied configuration.
        /// </summary>
        AllowedTypedefs = 1L << 36,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.allowedDelegates"/> from the supplied configuration.
        /// </summary>
        AllowedDelegates = 1L << 37,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.allowedConstants"/> from the supplied configuration.
        /// </summary>
        AllowedConstants = 1L << 38,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.generateRuntimeSource"/> from the supplied configuration.
        /// </summary>
        GenerateRuntimeSource = 1L << 39,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.constantMappings"/> from the supplied configuration.
        /// </summary>
        ConstantMappings = 1L << 40,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.enumMappings"/> from the supplied configuration.
        /// </summary>
        EnumMappings = 1L << 41,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.functionPrefixes"/>, <see cref="CsCodeGeneratorConfig.functionMappings"/> from the supplied configuration.
        /// </summary>
        FunctionMappings = 1L << 42,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.handleMappings"/> from the supplied configuration.
        /// </summary>
        HandleMappings = 1L << 43,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.classMappings"/> from the supplied configuration.
        /// </summary>
        ClassMappings = 1L << 44,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.delegateMappings"/> from the supplied configuration.
        /// </summary>
        DelegateMappings = 1L << 45,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.arrayMappings"/> from the supplied configuration.
        /// </summary>
        ArrayMappings = 1L << 46,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.nameMappings"/> from the supplied configuration.
        /// </summary>
        NameMappings = 1L << 47,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.typeMappings"/>, <see cref="CsCodeGeneratorConfig.externalTypeContracts"/> from the supplied configuration.
        /// </summary>
        TypeMappings = 1L << 48,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.usings"/> from the supplied configuration.
        /// </summary>
        Usings = 1L << 49,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.includeFolders"/> from the supplied configuration.
        /// </summary>
        IncludeFolders = 1L << 50,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.systemIncludeFolders"/> from the supplied configuration.
        /// </summary>
        SystemIncludeFolders = 1L << 51,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.defines"/> from the supplied configuration.
        /// </summary>
        Defines = 1L << 52,
        /// <summary>
        /// Combines <see cref="CsCodeGeneratorConfig.additionalArguments"/> from the supplied configuration.
        /// </summary>
        AdditionalArguments = 1L << 53,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.constantNamingConvention"/> from the supplied configuration.
        /// </summary>
        ConstantNamingConvention = 1L << 54,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.enumNamingConvention"/> from the supplied configuration.
        /// </summary>
        EnumNamingConvention = 1L << 55,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.enumItemNamingConvention"/> from the supplied configuration.
        /// </summary>
        EnumItemNamingConvention = 1L << 56,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.extensionNamingConvention"/> from the supplied configuration.
        /// </summary>
        ExtensionNamingConvention = 1L << 57,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.functionNamingConvention"/> from the supplied configuration.
        /// </summary>
        FunctionNamingConvention = 1L << 58,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.handleNamingConvention"/> from the supplied configuration.
        /// </summary>
        HandleNamingConvention = 1L << 59,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.typeNamingConvention"/> from the supplied configuration.
        /// </summary>
        TypeNamingConvention = 1L << 60,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.delegateNamingConvention"/> from the supplied configuration.
        /// </summary>
        DelegateNamingConvention = 1L << 61,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.parameterNamingConvention"/> from the supplied configuration.
        /// </summary>
        ParameterNamingConvention = 1L << 62,
        /// <summary>
        /// Copies <see cref="CsCodeGeneratorConfig.memberNamingConvention"/> from the supplied configuration.
        /// </summary>
        MemberNamingConvention = 1UL << 63,
        /// <summary>
        /// Merges all individually selectable configuration groups.
        /// </summary>
        All = ulong.MaxValue,
        /// <summary>
        /// Combines the allowed function, type, enum, typedef, delegate and constant filters.
        /// </summary>
        Allowed = AllowedFunctions | AllowedTypes | AllowedEnums | AllowedTypedefs | AllowedDelegates | AllowedConstants,
        /// <summary>
        /// Combines the ignored function, type, enum, typedef, delegate and constant filters.
        /// </summary>
        Ignored = IgnoredFunctions | IgnoredTypes | IgnoredEnums | IgnoredTypedefs | IgnoredDelegates | IgnoredConstants,
        /// <summary>
        /// Combines explicit symbol names, prefixes, default values, constructors and member-function declarations.
        /// </summary>
        Known = KnownConstantNames | KnownEnumValueNames | KnownEnumPrefixes | KnownExtensionPrefixes | KnownExtensionNames | KnownDefaultValueNames | KnownConstructors | KnownMemberFunctions,
        /// <summary>
        /// Combines explicit constant, enum, function, handle, class, delegate, array, name and type mappings.
        /// </summary>
        Mappings = ConstantMappings | EnumMappings | FunctionMappings | HandleMappings | ClassMappings | DelegateMappings | ArrayMappings | NameMappings | TypeMappings,
        /// <summary>
        /// Copies all category-specific identifier naming conventions.
        /// </summary>
        NamingConventions = ConstantNamingConvention | EnumNamingConvention | EnumItemNamingConvention | ExtensionNamingConvention | FunctionNamingConvention | HandleNamingConvention | TypeNamingConvention | DelegateNamingConvention | ParameterNamingConvention | MemberNamingConvention,
        /// <summary>
        /// Copies the grouped experimental generation and import settings.
        /// </summary>
        Experiments = EnableExperimentalOptions | GenerateConstructorsForStructs | DelegatesAsVoidPointer | WrapPointersAsHandle | GeneratePlaceholderComments | ImportType,
    }
}
