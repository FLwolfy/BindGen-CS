namespace BGCS.Configuration
{
    using System.Collections.Generic;

    /// <summary>
    /// Composes selected mutable configuration values under an explicit overwrite and concatenation policy.
    /// </summary>
    public partial class CsCodeGeneratorConfig
    {
        /// <summary>
        /// Merges only settings selected by the supplied option flags into this configuration.
        /// </summary>
        /// <param name="baseConfig">
        /// The source configuration; scalar values are copied and selected mutable descriptors retain the current merge policy.
        /// </param>
        /// <param name="mergeOptions">
        /// The setting-selection, overwrite, and concatenation flags applied to this merge.
        /// </param>
        public void Merge(
            CsCodeGeneratorConfig baseConfig,
            MergeOptions mergeOptions
        ) {
            if (mergeOptions.HasFlag(MergeOptions.EnableExperimentalOptions))
            {
                this.enableExperimentalOptions = baseConfig.enableExperimentalOptions;
            }

            if (mergeOptions.HasFlag(MergeOptions.GenerateSizeOfStructs))
            {
                this.generateSizeOfStructs = baseConfig.generateSizeOfStructs;
            }

            if (mergeOptions.HasFlag(MergeOptions.GenerateConstructorsForStructs))
            {
                this.generateConstructorsForStructs = baseConfig.generateConstructorsForStructs;
            }

            if (mergeOptions.HasFlag(MergeOptions.DelegatesAsVoidPointer))
            {
                this.delegatesAsVoidPointer = baseConfig.delegatesAsVoidPointer;
            }

            if (mergeOptions.HasFlag(MergeOptions.WrapPointersAsHandle))
            {
                this.wrapPointersAsHandle = baseConfig.wrapPointersAsHandle;
            }

            if (mergeOptions.HasFlag(MergeOptions.GeneratePlaceholderComments))
            {
                this.generatePlaceholderComments = baseConfig.generatePlaceholderComments;
            }

            if (mergeOptions.HasFlag(MergeOptions.ImportType))
            {
                this.importType = baseConfig.importType;
                this.emitLibraryNameConstant = baseConfig.emitLibraryNameConstant;
            }

            if (mergeOptions.HasFlag(MergeOptions.GenerateMetadata))
            {
                this.generateMetadata = baseConfig.generateMetadata;
            }

            if (mergeOptions.HasFlag(MergeOptions.GenerateConstants))
            {
                this.generateConstants = baseConfig.generateConstants;
            }

            if (mergeOptions.HasFlag(MergeOptions.GenerateEnums))
            {
                this.generateEnums = baseConfig.generateEnums;
            }

            if (mergeOptions.HasFlag(MergeOptions.GenerateExtensions))
            {
                this.generateExtensions = baseConfig.generateExtensions;
            }

            if (mergeOptions.HasFlag(MergeOptions.GenerateFunctions))
            {
                this.generateFunctions = baseConfig.generateFunctions;
            }

            if (mergeOptions.HasFlag(MergeOptions.GenerateHandles))
            {
                this.generateHandles = baseConfig.generateHandles;
            }

            if (mergeOptions.HasFlag(MergeOptions.GenerateTypes))
            {
                this.generateTypes = baseConfig.generateTypes;
            }

            if (mergeOptions.HasFlag(MergeOptions.GenerateDelegates))
            {
                this.generateDelegates = baseConfig.generateDelegates;
            }

            if (mergeOptions.HasFlag(MergeOptions.OneFilePerType))
            {
                this.oneFilePerType = baseConfig.oneFilePerType;
                this.nestGeneratedTypesInApi = baseConfig.nestGeneratedTypesInApi;
            }

            if (mergeOptions.HasFlag(MergeOptions.GenerateRuntimeSource))
            {
                this.generateRuntimeSource = baseConfig.generateRuntimeSource;
            }

            if (mergeOptions.HasFlag(MergeOptions.BoolType))
            {
                this.boolType = baseConfig.boolType;
            }

            MergeDictionaries(this.knownConstantNames, baseConfig.knownConstantNames, mergeOptions, MergeOptions.KnownConstantNames);
            MergeDictionaries(this.knownEnumValueNames, baseConfig.knownEnumValueNames, mergeOptions, MergeOptions.KnownEnumValueNames);
            MergeDictionaries(this.knownEnumPrefixes, baseConfig.knownEnumPrefixes, mergeOptions, MergeOptions.KnownEnumPrefixes);
            MergeDictionaries(this.knownExtensionPrefixes, baseConfig.knownExtensionPrefixes, mergeOptions, MergeOptions.KnownExtensionPrefixes);
            MergeDictionaries(this.knownExtensionNames, baseConfig.knownExtensionNames, mergeOptions, MergeOptions.KnownExtensionNames);
            MergeDictionaries(this.knownDefaultValueNames, baseConfig.knownDefaultValueNames, mergeOptions, MergeOptions.KnownDefaultValueNames);
            MergeDictionaries(this.knownConstructors, baseConfig.knownConstructors, mergeOptions, MergeOptions.KnownConstructors);
            MergeDictionaries(this.knownMemberFunctions, baseConfig.knownMemberFunctions, mergeOptions, MergeOptions.KnownMemberFunctions);
            MergeDictionaries(this.nameMappings, baseConfig.nameMappings, mergeOptions, MergeOptions.NameMappings);
            MergeDictionaries(this.typeMappings, baseConfig.typeMappings, mergeOptions, MergeOptions.TypeMappings);
            MergeHashSets(this.ignoredParts, baseConfig.ignoredParts, mergeOptions, MergeOptions.IgnoredParts);
            MergeHashSets(this.keywords, baseConfig.keywords, mergeOptions, MergeOptions.Keywords);
            MergeHashSets(this.ignoredFunctions, baseConfig.ignoredFunctions, mergeOptions, MergeOptions.IgnoredFunctions);
            MergeHashSets(this.ignoredTypes, baseConfig.ignoredTypes, mergeOptions, MergeOptions.IgnoredTypes);
            MergeHashSets(this.ignoredEnums, baseConfig.ignoredEnums, mergeOptions, MergeOptions.IgnoredEnums);
            MergeHashSets(this.ignoredTypedefs, baseConfig.ignoredTypedefs, mergeOptions, MergeOptions.IgnoredTypedefs);
            MergeHashSets(this.ignoredDelegates, baseConfig.ignoredDelegates, mergeOptions, MergeOptions.IgnoredDelegates);
            MergeHashSets(this.ignoredConstants, baseConfig.ignoredConstants, mergeOptions, MergeOptions.IgnoredConstants);
            MergeHashSets(this.allowedFunctions, baseConfig.allowedFunctions, mergeOptions, MergeOptions.AllowedFunctions);
            MergeHashSets(this.allowedTypes, baseConfig.allowedTypes, mergeOptions, MergeOptions.AllowedTypes);
            MergeHashSets(this.allowedEnums, baseConfig.allowedEnums, mergeOptions, MergeOptions.AllowedEnums);
            MergeHashSets(this.allowedTypedefs, baseConfig.allowedTypedefs, mergeOptions, MergeOptions.AllowedTypedefs);
            MergeHashSets(this.allowedDelegates, baseConfig.allowedDelegates, mergeOptions, MergeOptions.AllowedDelegates);
            MergeHashSets(this.allowedConstants, baseConfig.allowedConstants, mergeOptions, MergeOptions.AllowedConstants);
            MergeLists(this.constantMappings, baseConfig.constantMappings, mergeOptions, MergeOptions.ConstantMappings);
            MergeLists(this.enumMappings, baseConfig.enumMappings, mergeOptions, MergeOptions.EnumMappings);
            // FunctionPrefixes are part of function-name mapping and therefore share the
            // FunctionMappings merge flag. MergeOptions already occupies all 64 flag bits.
            MergeLists(this.functionPrefixes, baseConfig.functionPrefixes, mergeOptions, MergeOptions.FunctionMappings);
            MergeLists(this.functionMappings, baseConfig.functionMappings, mergeOptions, MergeOptions.FunctionMappings);
            MergeLists(this.handleMappings, baseConfig.handleMappings, mergeOptions, MergeOptions.HandleMappings);
            MergeLists(this.classMappings, baseConfig.classMappings, mergeOptions, MergeOptions.ClassMappings);
            // External carrier contracts refine TypeMappings and intentionally share its merge flag.
            MergeLists(this.externalTypeContracts, baseConfig.externalTypeContracts, mergeOptions, MergeOptions.TypeMappings);
            MergeLists(this.delegateMappings, baseConfig.delegateMappings, mergeOptions, MergeOptions.DelegateMappings);
            MergeLists(this.arrayMappings, baseConfig.arrayMappings, mergeOptions, MergeOptions.ArrayMappings);
            MergeLists(this.usings, baseConfig.usings, mergeOptions, MergeOptions.Usings);
            MergeLists(this.includeFolders, baseConfig.includeFolders, mergeOptions, MergeOptions.IncludeFolders);
            MergeLists(this.systemIncludeFolders, baseConfig.systemIncludeFolders, mergeOptions, MergeOptions.SystemIncludeFolders);
            MergeLists(this.defines, baseConfig.defines, mergeOptions, MergeOptions.Defines);
            MergeLists(this.additionalArguments, baseConfig.additionalArguments, mergeOptions, MergeOptions.AdditionalArguments);
            // Naming conventions to be merged
            if (mergeOptions.HasFlag(MergeOptions.ConstantNamingConvention))
            {
                this.constantNamingConvention = baseConfig.constantNamingConvention;
            }

            if (mergeOptions.HasFlag(MergeOptions.EnumNamingConvention))
            {
                this.enumNamingConvention = baseConfig.enumNamingConvention;
            }

            if (mergeOptions.HasFlag(MergeOptions.EnumItemNamingConvention))
            {
                this.enumItemNamingConvention = baseConfig.enumItemNamingConvention;
            }

            if (mergeOptions.HasFlag(MergeOptions.ExtensionNamingConvention))
            {
                this.extensionNamingConvention = baseConfig.extensionNamingConvention;
            }

            if (mergeOptions.HasFlag(MergeOptions.FunctionNamingConvention))
            {
                this.functionNamingConvention = baseConfig.functionNamingConvention;
            }

            if (mergeOptions.HasFlag(MergeOptions.HandleNamingConvention))
            {
                this.handleNamingConvention = baseConfig.handleNamingConvention;
            }

            if (mergeOptions.HasFlag(MergeOptions.TypeNamingConvention))
            {
                this.typeNamingConvention = baseConfig.typeNamingConvention;
            }

            if (mergeOptions.HasFlag(MergeOptions.DelegateNamingConvention))
            {
                this.delegateNamingConvention = baseConfig.delegateNamingConvention;
            }

            if (mergeOptions.HasFlag(MergeOptions.ParameterNamingConvention))
            {
                this.parameterNamingConvention = baseConfig.parameterNamingConvention;
            }

            if (mergeOptions.HasFlag(MergeOptions.MemberNamingConvention))
            {
                this.memberNamingConvention = baseConfig.memberNamingConvention;
            }
        }

        private static void MergeDictionaries<TKey, TValue>(
            Dictionary<TKey, TValue> target,
            Dictionary<TKey, TValue> source,
            MergeOptions mergeOptions,
            MergeOptions targetOption
        )
            where TKey : notnull
        {
            if (!mergeOptions.HasFlag(targetOption))
            {
                return;
            }

            foreach (var kvp in source)
            {
                target.TryAdd(kvp.Key, kvp.Value);
            }
        }

        private static void MergeHashSets<T>(
            HashSet<T> target,
            HashSet<T> source,
            MergeOptions mergeOptions,
            MergeOptions targetOption
        ) {
            if (!mergeOptions.HasFlag(targetOption))
            {
                return;
            }

            foreach (var item in source)
            {
                target.Add(item);
            }
        }

        private static void MergeLists<T>(
            List<T> target,
            List<T> source,
            MergeOptions mergeOptions,
            MergeOptions targetOption
        ) {
            if (!mergeOptions.HasFlag(targetOption))
            {
                return;
            }

            foreach (var item in source)
            {
                if (!target.Contains(item))
                {
                    target.Add(item);
                }
            }
        }
    }
}
