using System.Collections.Generic;
using System.Linq;

namespace BGCS.Cpp2C.Configuration
{
    using System;
    using System.Text;
    using BGCS.Cpp2C.Lowering;
    using BGCS.CppAst.Extensions;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Templates;
    using BGCS.CppAst.Model.Types;

    /// <summary>
    /// Configures C++ facade lowering, callable selection, C ABI spellings, and generated bridge names.
    /// </summary>
    public partial class Cpp2CGeneratorConfig
    {
        private readonly Dictionary<CppType, string> m_sourceTypeSpellings = new(ReferenceEqualityComparer.Instance);
        internal void ClearAnalysisReferences() => this.m_sourceTypeSpellings.Clear();
        internal void RegisterSourceTypeSpelling(
            CppType type,
            string spelling
        ) {
            if (!string.IsNullOrWhiteSpace(spelling))
                this.m_sourceTypeSpellings[type] = spelling.Trim();
        }

        internal string GetCppValueTypeSpelling(CppType type)
        {
            if (this.m_sourceTypeSpellings.TryGetValue(type, out string? spelling))
            {
                string normalized = NormalizeValueTypeSpelling(spelling);
                CppType registeredType = UnwrapReferenceAndQualification(type);
                if (registeredType is CppTypedef registeredAlias && !string.IsNullOrEmpty(registeredAlias.fullParentName) && string.Equals(normalized, registeredAlias.name, StringComparison.Ordinal))
                    return registeredAlias.fullParentName + "::" + registeredAlias.name;
                return normalized;
            }

            CppType current = UnwrapReferenceAndQualification(type);
            while (current is CppTypedef typedef)
            {
                if (!string.IsNullOrEmpty(typedef.fullParentName))
                    return typedef.fullParentName + "::" + typedef.name;
                current = UnwrapReferenceAndQualification(typedef.elementType);
            }

            return current is CppClass cppClass ? cppClass.fullName : current.GetDisplayName();
        }

        /// <summary>
        /// Projects a native type into the C ABI spelling selected by registered lowering or supported native type shapes.
        /// </summary>
        /// <param name="type">
        /// The non-null native type descriptor from the current compilation.
        /// </param>
        /// <returns>
        /// The lowered C ABI type spelling, including pointer and qualifier syntax.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The type descriptor is null.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// The type has no safe lowering or supported C ABI representation.
        /// </exception>
        public string GetCType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            CppTypeLoweringPlan? lowering = ResolveTypeLowering(type, CppTypeLoweringUse.Field);
            if (lowering != null)
                return lowering.cAbiType;
            CppType unwrapped = UnwrapReferenceAndQualification(type);
            string displayName = unwrapped is CppClass standardClass ? standardClass.fullName : unwrapped.GetDisplayName();
            if (displayName.Replace(" ", string.Empty, StringComparison.Ordinal).StartsWith("std::", StringComparison.Ordinal) && displayName.Contains('<'))
                throw new NotSupportedException($"Standard-library specialization '{displayName}' has no registered safe adapter.");
            return type switch
            {
                CppPrimitiveType primitive => GetPrimitiveName(primitive),
                CppPointerType pointer => GetCType(pointer.elementType) + "*",
                CppReferenceType reference => GetCType(reference.elementType) + "*",
                CppArrayType array => GetCType(array.elementType) + "*",
                CppQualifiedType qualified => GetQualifiedCType(qualified),
                CppTypedef typedef => IsFunctionPointerTypedef(typedef) ? "void*" : typedef.name,
                CppClass cppClass => GetCTypeName(cppClass),
                CppEnum cppEnum => GetCTypeName(cppEnum),
                CppFunctionType => "void*",
                _ => throw new NotSupportedException($"C bridge type '{type}' ({type.typeKind}) is not supported.")
            };
        }

        /// <summary>
        /// Resolves the first registered type lowering applicable to the requested usage boundary.
        /// </summary>
        /// <param name="type">
        /// The non-null native type descriptor.
        /// </param>
        /// <param name="use">
        /// The field, parameter, or return boundary at which the type will cross the bridge.
        /// </param>
        /// <returns>
        /// The matched immutable lowering plan, or null when no provider accepts the type.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The type descriptor is null.
        /// </exception>
        public CppTypeLoweringPlan? ResolveTypeLowering(
            CppType type,
            CppTypeLoweringUse use
        ) {
            ArgumentNullException.ThrowIfNull(type);
            return this.lowerings.TryResolve(type, new(this, use), out CppTypeLoweringPlan? plan) ? plan : null;
        }

        /// <summary>
        /// Resolves callable selection, naming, and invocation rules from the registered lowering catalog.
        /// </summary>
        /// <param name="declaringType">
        /// The owning native class, or null for a free function.
        /// </param>
        /// <param name="function">
        /// The non-null callable declaration to classify.
        /// </param>
        /// <param name="defaultExportName">
        /// The inferred C export identifier supplied when no provider renames the callable.
        /// </param>
        /// <returns>
        /// The matched immutable callable plan, or null when no provider accepts the declaration.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The function descriptor is null.
        /// </exception>
        public CppCallableLoweringPlan? ResolveCallableLowering(
            CppClass? declaringType,
            CppFunction function,
            string defaultExportName
        ) {
            ArgumentNullException.ThrowIfNull(function);
            return this.lowerings.TryResolve(function, new(this, declaringType, defaultExportName), out CppCallableLoweringPlan? plan) ? plan : null;
        }

        /// <summary>
        /// Applies a registered callable rename or retains the inferred C export identifier.
        /// </summary>
        /// <param name="declaringType">
        /// The owning native class, or null for a free function.
        /// </param>
        /// <param name="function">
        /// The non-null callable declaration to classify.
        /// </param>
        /// <param name="defaultExportName">
        /// The inferred C export identifier supplied when no provider renames the callable.
        /// </param>
        /// <returns>
        /// The final C export identifier after callable lowering.
        /// </returns>
        public string GetCFunctionName(
            CppClass? declaringType,
            CppFunction function,
            string defaultExportName
        ) {
            CppCallableLoweringPlan? plan = ResolveCallableLowering(declaringType, function, defaultExportName);
            return plan?.exportName ?? defaultExportName;
        }

        /// <summary>
        /// Checks whether registered callable lowering removes the declaration from bridge generation.
        /// </summary>
        /// <param name="declaringType">
        /// The owning native class, or null for a free function.
        /// </param>
        /// <param name="function">
        /// The non-null callable declaration to classify.
        /// </param>
        /// <param name="defaultExportName">
        /// The inferred C export identifier supplied when no provider renames the callable.
        /// </param>
        /// <returns>
        /// True only when a resolved callable plan explicitly excludes the declaration.
        /// </returns>
        public bool IsCallableExcluded(
            CppClass? declaringType,
            CppFunction function,
            string defaultExportName
        ) => ResolveCallableLowering(declaringType, function, defaultExportName)?.exclude == true;
        internal string ApplyCallableInvocation(
            CppClass? declaringType,
            CppFunction function,
            string defaultExportName,
            string invocation
        ) {
            CppCallableLoweringPlan? plan = ResolveCallableLowering(declaringType, function, defaultExportName);
            return string.IsNullOrWhiteSpace(plan?.invocationExpression) ? invocation : plan.invocationExpression.Replace("{invocation}", invocation, StringComparison.Ordinal);
        }

        /// <summary>
        /// Checks registered lowering and configured native type names for borrowed UTF-8 text.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsUtf8StringType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeLoweringKind.Utf8String))
                return true;
            return IsUtf8StringTypeCore(type);
        }

        internal bool IsUtf8StringTypeCore(CppType type)
        {
            CppType current = type;
            while (current is CppQualifiedType or CppReferenceType)
                current = ((CppTypeWithElementType)current).elementType;
            string name = current switch
            {
                CppClass cppClass => cppClass.fullName,
                CppTypedef typedef => string.IsNullOrEmpty(typedef.fullParentName) ? typedef.name : typedef.fullParentName + "::" + typedef.name,
                CppUnexposedType unexposed => unexposed.name,
                _ => current.GetDisplayName()
            };
            string compactName = name.Replace(" ", string.Empty, StringComparison.Ordinal);
            return this.utf8StringTypes.Any(candidate =>
            {
                string configured = candidate.Replace(" ", string.Empty, StringComparison.Ordinal);
                return string.Equals(compactName, configured, StringComparison.Ordinal) || compactName.StartsWith(configured + "<", StringComparison.Ordinal) || configured == "std::basic_string<char>" && compactName.StartsWith("std::basic_string<char,", StringComparison.Ordinal);
            });
        }

        /// <summary>
        /// Checks registered lowering and configured native type names for unique ownership.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsUniquePtrType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeLoweringKind.UniqueOwner))
                return true;
            return IsUniquePtrTypeCore(type);
        }

        internal bool IsUniquePtrTypeCore(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string compactName = current.GetDisplayName().Replace(" ", string.Empty, StringComparison.Ordinal);
            if (current is CppClass cppClass)
                compactName = cppClass.fullName.Replace(" ", string.Empty, StringComparison.Ordinal);
            return this.uniquePtrTypes.Any(candidate => compactName.StartsWith(candidate.Replace(" ", string.Empty, StringComparison.Ordinal) + "<", StringComparison.Ordinal));
        }

        /// <summary>
        /// Checks registered lowering and configured native type names for shared ownership.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsSharedPtrType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeLoweringKind.SharedOwner))
                return true;
            return IsSharedPtrTypeCore(type);
        }

        internal bool IsSharedPtrTypeCore(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string compactName = current is CppClass cppClass ? cppClass.fullName.Replace(" ", string.Empty, StringComparison.Ordinal) : current.GetDisplayName().Replace(" ", string.Empty, StringComparison.Ordinal);
            return this.sharedPtrTypes.Any(candidate => compactName.StartsWith(candidate.Replace(" ", string.Empty, StringComparison.Ordinal) + "<", StringComparison.Ordinal));
        }

        /// <summary>
        /// Builds the prefixed C holder identifier for a configured shared-owner element type.
        /// </summary>
        /// <param name="type">
        /// The shared-owner native type with a modeled type argument.
        /// </param>
        /// <returns>
        /// The sanitized holder name derived from the element C ABI spelling.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// The shared-owner element type cannot be resolved or projected to the C ABI.
        /// </exception>
        public string GetSharedPtrHolderName(CppType type)
        {
            if (!TryGetTemplateElementType(type, out CppType? elementType))
                throw new NotSupportedException($"Unable to resolve shared_ptr element type '{type}'.");
            return this.namePrefix + "SharedPtr_" + SanitizeCIdentifier(GetCType(elementType!));
        }

        /// <summary>
        /// Checks registered lowering and configured native type names for contiguous borrowed spans.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsSpanType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeLoweringKind.Span))
                return true;
            return IsSpanTypeCore(type);
        }

        internal bool IsSpanTypeCore(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string compactName = current is CppClass cppClass ? cppClass.fullName.Replace(" ", string.Empty, StringComparison.Ordinal) : current.GetDisplayName().Replace(" ", string.Empty, StringComparison.Ordinal);
            return this.spanTypes.Any(candidate => compactName.StartsWith(candidate.Replace(" ", string.Empty, StringComparison.Ordinal) + "<", StringComparison.Ordinal));
        }

        /// <summary>
        /// Checks registered lowering and configured native type names for owned contiguous vectors.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsVectorType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeLoweringKind.Vector))
                return true;
            return IsVectorTypeCore(type);
        }

        internal bool IsVectorTypeCore(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string compactName = current is CppClass cppClass ? cppClass.fullName.Replace(" ", string.Empty, StringComparison.Ordinal) : current.GetDisplayName().Replace(" ", string.Empty, StringComparison.Ordinal);
            return this.vectorTypes.Any(candidate => compactName.StartsWith(candidate.Replace(" ", string.Empty, StringComparison.Ordinal) + "<", StringComparison.Ordinal));
        }

        /// <summary>
        /// Checks registered lowering and configured native type names for optional values.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsOptionalType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeLoweringKind.Optional))
                return true;
            return IsOptionalTypeCore(type);
        }

        internal bool IsOptionalTypeCore(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string compactName = current is CppClass cppClass ? cppClass.fullName.Replace(" ", string.Empty, StringComparison.Ordinal) : current.GetDisplayName().Replace(" ", string.Empty, StringComparison.Ordinal);
            return this.optionalTypes.Any(candidate => compactName.StartsWith(candidate.Replace(" ", string.Empty, StringComparison.Ordinal) + "<", StringComparison.Ordinal));
        }

        /// <summary>
        /// Checks registered lowering and configured native type names for fixed-size arrays.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsArrayType(CppType type) => ResolveCustomKind(type, CppTypeLoweringKind.Array) || IsArrayTypeCore(type);
        internal bool IsArrayTypeCore(CppType type) => IsConfiguredType(type, this.arrayTypes, requireTemplate: true);
        /// <summary>
        /// Checks registered lowering and configured native type names for opaque maps.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsMapType(CppType type) => ResolveCustomKind(type, CppTypeLoweringKind.Map) || IsMapTypeCore(type);
        internal bool IsMapTypeCore(CppType type) => IsConfiguredType(type, this.mapTypes, requireTemplate: true);
        /// <summary>
        /// Checks registered lowering and configured native type names for opaque sets.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsSetType(CppType type) => ResolveCustomKind(type, CppTypeLoweringKind.Set) || IsSetTypeCore(type);
        internal bool IsSetTypeCore(CppType type) => IsConfiguredType(type, this.setTypes, requireTemplate: true);
        /// <summary>
        /// Checks registered lowering and configured native type names for tagged variants.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsVariantType(CppType type) => ResolveCustomKind(type, CppTypeLoweringKind.Variant) || IsVariantTypeCore(type);
        internal bool IsVariantTypeCore(CppType type) => IsConfiguredType(type, this.variantTypes, requireTemplate: true);
        /// <summary>
        /// Checks registered lowering and configured native type names for value-or-error results.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsExpectedType(CppType type) => ResolveCustomKind(type, CppTypeLoweringKind.Expected) || IsExpectedTypeCore(type);
        internal bool IsExpectedTypeCore(CppType type) => IsConfiguredType(type, this.expectedTypes, requireTemplate: true);
        /// <summary>
        /// Checks registered lowering and configured native type names for filesystem paths.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsPathType(CppType type) => ResolveCustomKind(type, CppTypeLoweringKind.Path) || IsPathTypeCore(type);
        internal bool IsPathTypeCore(CppType type) => IsConfiguredType(type, this.pathTypes, requireTemplate: false);
        /// <summary>
        /// Checks registered lowering and configured native type names for chrono durations.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsChronoDurationType(CppType type) => ResolveCustomKind(type, CppTypeLoweringKind.ChronoDuration) || IsChronoDurationTypeCore(type);
        internal bool IsChronoDurationTypeCore(CppType type) => IsConfiguredType(type, this.chronoDurationTypes, requireTemplate: true);
        /// <summary>
        /// Checks registered lowering and configured native type names for chrono time points.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor to classify through supported alias, reference, and qualification shapes.
        /// </param>
        /// <returns>
        /// True when a custom lowering or the configured built-in type policy matches this category.
        /// </returns>
        public bool IsChronoTimePointType(CppType type) => ResolveCustomKind(type, CppTypeLoweringKind.ChronoTimePoint) || IsChronoTimePointTypeCore(type);
        internal bool IsChronoTimePointTypeCore(CppType type) => IsConfiguredType(type, this.chronoTimePointTypes, requireTemplate: true);
        /// <summary>
        /// Collects native type arguments from the parser model without guessing layouts from source spellings.
        /// </summary>
        /// <param name="type">
        /// The native specialization, optionally wrapped in references, qualifiers, or typedefs.
        /// </param>
        /// <returns>
        /// An ordered target-specific type-argument snapshot; non-type arguments are omitted and unsupported shapes return an empty list.
        /// </returns>
        public IReadOnlyList<CppType> GetTemplateTypeArguments(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            while (current is CppTypedef typedef)
                current = UnwrapReferenceAndQualification(typedef.elementType);
            return current switch
            {
                CppClass cppClass => cppClass.templateSpecializedArguments
                    .Where(argument => argument.argAsType != null)
                    .Select(argument => argument.argAsType!).ToArray(),
                CppUnexposedType unexposed => unexposed.templateParameters.ToArray(),
                _ => []
            };
        }

        /// <summary>
        /// Resolves the first nonnegative integral template argument or recorded fixed-array extent.
        /// </summary>
        /// <param name="type">
        /// The configured fixed-size native array specialization.
        /// </param>
        /// <returns>
        /// The nonnegative element count of the modeled or recorded specialization.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// No supported nonnegative fixed extent is available.
        /// </exception>
        public long GetArrayElementCount(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            if (current is CppClass cppClass)
            {
                CppTemplateArgument? count = cppClass.templateSpecializedArguments.FirstOrDefault(argument => argument.argKind == CppTemplateArgumentKind.AsInteger);
                if (count != null && count.argAsInteger >= 0)
                    return count.argAsInteger;
            }

            if (this.m_sourceTypeSpellings.TryGetValue(type, out string? spelling))
            {
                string[] arguments = SplitTemplateArguments(NormalizeValueTypeSpelling(spelling));
                if (arguments.Length > 1 && long.TryParse(arguments[1], out long parsed) && parsed >= 0)
                    return parsed;
            }

            throw new NotSupportedException($"Unable to resolve fixed array extent for '{type}'.");
        }

        /// <summary>
        /// Builds a stable prefixed C holder name from the recorded or modeled native value spelling.
        /// </summary>
        /// <param name="type">
        /// The native value descriptor whose bridge holder name is needed.
        /// </param>
        /// <returns>
        /// The sanitized C holder identifier for this native specialization.
        /// </returns>
        public string GetOpaqueValueHolderName(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string displayName = this.m_sourceTypeSpellings.TryGetValue(type, out string? spelling) ? NormalizeValueTypeSpelling(spelling) : current is CppClass cppClass ? cppClass.fullName : current.GetDisplayName();
            return this.namePrefix + "Value_" + SanitizeCIdentifier(displayName);
        }

        internal void ValidateOpaqueLoweringArguments(
            CppType type,
            CppTypeLoweringKind kind
        ) {
            IReadOnlyList<CppType> arguments = GetTemplateTypeArguments(type);
            int required = kind is CppTypeLoweringKind.Map or CppTypeLoweringKind.Expected ? 2 : 1;
            if (arguments.Count < required)
                throw new NotSupportedException($"Adapter '{kind}' cannot resolve the required template arguments for '{type}'.");
            int count = kind switch
            {
                CppTypeLoweringKind.Map or CppTypeLoweringKind.Expected => 2,
                CppTypeLoweringKind.Set => 1,
                _ => arguments.Count
            };
            for (int index = 0; index < count; index++)
            {
                if (arguments[index] is CppPrimitiveType { kind: CppPrimitiveKind.Void } || !IsBlittableBridgeType(arguments[index]))
                    throw new NotSupportedException($"Lowering '{kind}' requires ABI-value template arguments; argument {index} of '{type}' is '{arguments[index]}'. Register a custom lowering with an explicit ownership protocol.");
            }
        }

        private bool IsConfiguredType(
            CppType type,
            IEnumerable<string> configuredNames,
            bool requireTemplate
        ) {
            if (this.m_sourceTypeSpellings.TryGetValue(type, out string? sourceSpelling) && MatchesConfiguredName(NormalizeValueTypeSpelling(sourceSpelling), configuredNames, requireTemplate))
                return true;
            CppType current = UnwrapReferenceAndQualification(type);
            while (true)
            {
                string name = current switch
                {
                    CppClass cppClass => cppClass.fullName,
                    CppTypedef typedef when !string.IsNullOrEmpty(typedef.fullParentName) => typedef.fullParentName + "::" + typedef.name,
                    _ => current.GetDisplayName()
                };
                if (MatchesConfiguredName(name, configuredNames, requireTemplate))
                    return true;
                if (current is not CppTypedef alias)
                    return false;
                current = UnwrapReferenceAndQualification(alias.elementType);
            }
        }

        private static bool MatchesConfiguredName(
            string name,
            IEnumerable<string> configuredNames,
            bool requireTemplate
        ) {
            string compactName = name.Replace(" ", string.Empty, StringComparison.Ordinal);
            return configuredNames.Any(candidate =>
            {
                string configured = candidate.Replace(" ", string.Empty, StringComparison.Ordinal);
                return requireTemplate ? compactName.StartsWith(configured + "<", StringComparison.Ordinal) : string.Equals(compactName, configured, StringComparison.Ordinal) || compactName.StartsWith(configured + "<", StringComparison.Ordinal);
            });
        }

        private static string NormalizeValueTypeSpelling(string spelling)
        {
            string result = spelling.Trim();
            while (result.StartsWith("const ", StringComparison.Ordinal) || result.StartsWith("volatile ", StringComparison.Ordinal))
                result = result[(result.IndexOf(' ') + 1)..].TrimStart();
            while (result.EndsWith('&') || result.EndsWith(' '))
                result = result[..^1].TrimEnd();
            return result;
        }

        private static string[] SplitTemplateArguments(string spelling)
        {
            int open = spelling.IndexOf('<');
            int close = spelling.LastIndexOf('>');
            if (open < 0 || close <= open)
                return [];
            string body = spelling[(open + 1)..close];
            List<string> arguments = [];
            int depth = 0;
            int start = 0;
            for (int index = 0; index < body.Length; index++)
            {
                if (body[index] == '<')
                    depth++;
                else if (body[index] == '>')
                    depth--;
                else if (body[index] == ',' && depth == 0)
                {
                    arguments.Add(body[start..index].Trim());
                    start = index + 1;
                }
            }

            arguments.Add(body[start..].Trim());
            return arguments.ToArray();
        }

        private bool ResolveCustomKind(
            CppType type,
            CppTypeLoweringKind kind
        ) => this.lowerings.TryResolve(type, new(this, CppTypeLoweringUse.Field), out CppTypeLoweringPlan? plan) && plan!.kind == kind;
        /// <summary>
        /// Attempts to resolve the first type argument of a configured smart pointer, view, or optional type.
        /// </summary>
        /// <param name = "type">Smart pointer type.</param>
        /// <param name = "elementType">Receives the first type argument.</param>
        /// <returns><see langword="true"/> when a type argument is available.</returns>
        public bool TryGetTemplateElementType(
            CppType type,
            out CppType? elementType
        ) {
            CppType current = UnwrapReferenceAndQualification(type);
            if (current is CppClass { templateSpecializedArguments.Count: > 0 } cppClass && cppClass.templateSpecializedArguments[0].argAsType is CppType argumentType)
            {
                elementType = argumentType;
                return true;
            }

            elementType = null;
            return false;
        }

        private static CppType UnwrapReferenceAndQualification(CppType type)
        {
            CppType current = type;
            while (current is CppQualifiedType or CppReferenceType)
                current = ((CppTypeWithElementType)current).elementType;
            return current;
        }

        private static bool IsFunctionPointerTypedef(CppTypedef typedef) => IsFunctionPointerType(typedef.elementType);
        /// <summary>
        /// Determines whether a native type represents a function pointer, including typedef chains.
        /// </summary>
        /// <param name = "type">Type to inspect.</param>
        /// <returns><see langword="true"/> for function-pointer shapes.</returns>
        public static bool IsFunctionPointerType(CppType type)
        {
            CppType current = type;
            while (current is CppTypedef nested)
                current = nested.elementType;
            return current is CppFunctionType || current is CppPointerType { elementType: CppFunctionType };
        }

        /// <summary>
        /// Determines whether a type can cross the generated C ABI by value without a lifetime protocol.
        /// </summary>
        /// <param name = "type">Type to inspect.</param>
        /// <returns><see langword="true"/> for primitive, enum, and pointer-shaped values.</returns>
        public bool IsBlittableBridgeType(CppType type)
        {
            return type switch
            {
                CppPrimitiveType or CppEnum or CppPointerType or CppReferenceType or CppFunctionType => true,
                CppQualifiedType qualified => IsBlittableBridgeType(qualified.elementType),
                CppTypedef typedef => IsBlittableBridgeType(typedef.elementType),
                _ => false
            };
        }

        private string GetQualifiedCType(CppQualifiedType qualifiedType)
        {
            string qualifier = qualifiedType.qualifier switch
            {
                CppTypeQualifier.Const => "const ",
                CppTypeQualifier.Volatile => "volatile ",
                _ => string.Empty
            };
            return qualifier + GetCType(qualifiedType.elementType);
        }

        private static string GetPrimitiveName(CppPrimitiveType primitiveType)
        {
            return primitiveType.kind switch
            {
                CppPrimitiveKind.Void => "void",
                CppPrimitiveKind.Bool => "bool",
                CppPrimitiveKind.WChar => "wchar_t",
                CppPrimitiveKind.Char => "char",
                CppPrimitiveKind.Short => "short",
                CppPrimitiveKind.Int => "int",
                CppPrimitiveKind.Long => "long",
                CppPrimitiveKind.LongLong => "long long",
                CppPrimitiveKind.UnsignedChar => "unsigned char",
                CppPrimitiveKind.UnsignedShort => "unsigned short",
                CppPrimitiveKind.UnsignedInt => "unsigned int",
                CppPrimitiveKind.UnsignedLong => "unsigned long",
                CppPrimitiveKind.UnsignedLongLong => "unsigned long long",
                CppPrimitiveKind.Float => "float",
                CppPrimitiveKind.Double => "double",
                CppPrimitiveKind.LongDouble => "long double",
                CppPrimitiveKind.ObjCId => "void*",
                CppPrimitiveKind.ObjCSel => "void*",
                CppPrimitiveKind.ObjCClass => "void*",
                CppPrimitiveKind.ObjCObject => "void*",
                CppPrimitiveKind.Int128 => "__int128",
                CppPrimitiveKind.UInt128 => "unsigned __int128",
                CppPrimitiveKind.Float16 => "_Float16",
                CppPrimitiveKind.BFloat16 => "unsigned short",
                _ => throw new NotSupportedException(),
            };
        }

        /// <summary>
        /// Builds the prefixed C type name, sanitizing specialization spellings when needed.
        /// </summary>
        /// <param name="c">
        /// The native class or struct declaration to project.
        /// </param>
        /// <returns>
        /// The configured prefix followed by the native name or sanitized specialization spelling.
        /// </returns>
        public string GetCTypeName(CppClass c)
        {
            string name = c.templateKind == CppTemplateKind.TemplateSpecializedClass ? SanitizeCIdentifier(c.fullName) : c.name;
            return this.namePrefix + name;
        }

        /// <summary>
        /// Returns the stable C identifier generated for a C++ free function.
        /// </summary>
        /// <param name = "cppFunction">Source function declaration.</param>
        /// <returns>A namespace-qualified and prefix-qualified C identifier.</returns>
        public string GetCFunctionName(CppFunction cppFunction)
        {
            string parent = cppFunction.fullParentName.Replace("::", "_", StringComparison.Ordinal);
            string name = string.IsNullOrEmpty(parent) ? cppFunction.name : parent + "_" + cppFunction.name;
            string defaultName = this.namePrefix + SanitizeCIdentifier(name);
            return GetCFunctionName(null, cppFunction, defaultName);
        }

        /// <summary>
        /// Returns the stable C identifier generated for a C++ enum.
        /// </summary>
        /// <param name = "cppEnum">Source enum declaration.</param>
        /// <returns>A namespace-qualified and prefix-qualified C identifier.</returns>
        public string GetCTypeName(CppEnum cppEnum)
        {
            string parent = cppEnum.fullParentName.Replace("::", "_", StringComparison.Ordinal);
            string name = string.IsNullOrEmpty(parent) ? cppEnum.name : parent + "_" + cppEnum.name;
            return this.namePrefix + SanitizeCIdentifier(name);
        }

        private static string SanitizeCIdentifier(string value)
        {
            StringBuilder builder = new(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool valid = character == '_' || char.IsAsciiLetter(character) || i > 0 && char.IsAsciiDigit(character);
                builder.Append(valid ? character : '_');
            }

            if (builder.Length == 0 || char.IsAsciiDigit(builder[0]))
            {
                builder.Insert(0, '_');
            }

            return builder.ToString();
        }
    }
}
