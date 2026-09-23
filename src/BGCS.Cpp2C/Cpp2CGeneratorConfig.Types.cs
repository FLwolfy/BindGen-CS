namespace BGCS.Cpp2C
{
    using BGCS.Core.CSharp;
    using BGCS.CppAst.Extensions;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Templates;
    using BGCS.CppAst.Model.Types;
    using BGCS.Cpp2C.Adapters;
    using BGCS.Intermediate;
    using System;
    using System.Text;

    /// <summary>
    /// Defines the public class <c>Cpp2CGeneratorConfig</c> used by the generation pipeline.
    /// </summary>
    public partial class Cpp2CGeneratorConfig
    {
        private readonly Dictionary<CppType, string> sourceTypeSpellings = new(ReferenceEqualityComparer.Instance);

        internal void RegisterSourceTypeSpelling(CppType type, string spelling)
        {
            if (!string.IsNullOrWhiteSpace(spelling))
                sourceTypeSpellings[type] = spelling.Trim();
        }

        internal string GetCppValueTypeSpelling(CppType type)
        {
            if (sourceTypeSpellings.TryGetValue(type, out string? spelling))
            {
                string normalized = NormalizeValueTypeSpelling(spelling);
                CppType registeredType = UnwrapReferenceAndQualification(type);
                if (registeredType is CppTypedef registeredAlias &&
                    !string.IsNullOrEmpty(registeredAlias.FullParentName) &&
                    string.Equals(normalized, registeredAlias.Name, StringComparison.Ordinal))
                    return registeredAlias.FullParentName + "::" + registeredAlias.Name;
                return normalized;
            }
            CppType current = UnwrapReferenceAndQualification(type);
            while (current is CppTypedef typedef)
            {
                if (!string.IsNullOrEmpty(typedef.FullParentName))
                    return typedef.FullParentName + "::" + typedef.Name;
                current = UnwrapReferenceAndQualification(typedef.ElementType);
            }
            return current is CppClass cppClass ? cppClass.FullName : current.GetDisplayName();
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetCType</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetCType</c>.</returns>
        public string GetCType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            CppTypeAdapterPlan? adapter = ResolveTypeAdapter(type, CppTypeAdapterUse.Field);
            if (adapter != null)
                return adapter.CAbiType;
            CppType unwrapped = UnwrapReferenceAndQualification(type);
            string displayName = unwrapped is CppClass standardClass ? standardClass.FullName : unwrapped.GetDisplayName();
            if (displayName.Replace(" ", string.Empty, StringComparison.Ordinal).StartsWith("std::", StringComparison.Ordinal) && displayName.Contains('<'))
                throw new NotSupportedException($"Standard-library specialization '{displayName}' has no registered safe adapter.");
            return type switch
            {
                CppPrimitiveType primitive => GetPrimitiveName(primitive),
                CppPointerType pointer => GetCType(pointer.ElementType) + "*",
                CppReferenceType reference => GetCType(reference.ElementType) + "*",
                CppArrayType array => GetCType(array.ElementType) + "*",
                CppQualifiedType qualified => GetQualifiedCType(qualified),
                CppTypedef typedef => IsFunctionPointerTypedef(typedef) ? "void*" : typedef.Name,
                CppClass cppClass => GetCTypeName(cppClass),
                CppEnum cppEnum => GetCTypeName(cppEnum),
                CppFunctionType => "void*",
                _ => throw new NotSupportedException($"C bridge type '{type}' ({type.TypeKind}) is not supported.")
            };
        }

        /// <summary>
        /// Resolves a registered or built-in semantic adapter for a C++ type.
        /// </summary>
        public CppTypeAdapterPlan? ResolveTypeAdapter(CppType type, CppTypeAdapterUse use)
        {
            ArgumentNullException.ThrowIfNull(type);
            return Adapters.TryResolve(type, new(this, use), out CppTypeAdapterPlan? plan) ? plan : null;
        }

        /// <summary>Resolves third-party callable selection and naming.</summary>
        public CppCallableAdapterPlan? ResolveCallableAdapter(CppClass? declaringType, CppFunction function, string defaultExportName)
        {
            ArgumentNullException.ThrowIfNull(function);
            return Adapters.TryResolve(function, new(this, declaringType, defaultExportName), out CppCallableAdapterPlan? plan)
                ? plan
                : null;
        }

        /// <summary>Returns the final exported name for a free function or class method.</summary>
        public string GetCFunctionName(CppClass? declaringType, CppFunction function, string defaultExportName)
        {
            CppCallableAdapterPlan? plan = ResolveCallableAdapter(declaringType, function, defaultExportName);
            return plan?.ExportName ?? defaultExportName;
        }

        /// <summary>Determines whether a callable adapter excludes a declaration.</summary>
        public bool IsCallableExcluded(CppClass? declaringType, CppFunction function, string defaultExportName) =>
            ResolveCallableAdapter(declaringType, function, defaultExportName)?.Exclude == true;

        /// <summary>
        /// Determines whether a C++ type uses the configured borrowed UTF-8 string adapter.
        /// </summary>
        /// <param name="type">C++ type to inspect.</param>
        /// <returns><see langword="true"/> when the type is configured as a UTF-8 string.</returns>
        public bool IsUtf8StringType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeAdapterKind.Utf8String)) return true;
            return IsUtf8StringTypeCore(type);
        }

        internal bool IsUtf8StringTypeCore(CppType type)
        {
            CppType current = type;
            while (current is CppQualifiedType or CppReferenceType)
                current = ((CppTypeWithElementType)current).ElementType;
            string name = current switch
            {
                CppClass cppClass => cppClass.FullName,
                CppTypedef typedef => string.IsNullOrEmpty(typedef.FullParentName)
                    ? typedef.Name
                    : typedef.FullParentName + "::" + typedef.Name,
                CppUnexposedType unexposed => unexposed.Name,
                _ => current.GetDisplayName()
            };
            string compactName = name.Replace(" ", string.Empty, StringComparison.Ordinal);
            return Utf8StringTypes.Any(candidate =>
            {
                string configured = candidate.Replace(" ", string.Empty, StringComparison.Ordinal);
                return string.Equals(compactName, configured, StringComparison.Ordinal) ||
                    compactName.StartsWith(configured + "<", StringComparison.Ordinal) ||
                    configured == "std::basic_string<char>" && compactName.StartsWith("std::basic_string<char,", StringComparison.Ordinal);
            });
        }

        /// <summary>
        /// Determines whether a C++ type uses the configured unique-owner adapter.
        /// </summary>
        /// <param name="type">C++ type to inspect.</param>
        /// <returns><see langword="true"/> when the type is a configured unique pointer specialization.</returns>
        public bool IsUniquePtrType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeAdapterKind.UniqueOwner)) return true;
            return IsUniquePtrTypeCore(type);
        }

        internal bool IsUniquePtrTypeCore(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string compactName = current.GetDisplayName().Replace(" ", string.Empty, StringComparison.Ordinal);
            if (current is CppClass cppClass)
                compactName = cppClass.FullName.Replace(" ", string.Empty, StringComparison.Ordinal);
            return UniquePtrTypes.Any(candidate => compactName.StartsWith(
                candidate.Replace(" ", string.Empty, StringComparison.Ordinal) + "<", StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether a C++ type uses the configured shared-owner adapter.
        /// </summary>
        /// <param name="type">C++ type to inspect.</param>
        /// <returns><see langword="true"/> when the type is a configured shared pointer specialization.</returns>
        public bool IsSharedPtrType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeAdapterKind.SharedOwner)) return true;
            return IsSharedPtrTypeCore(type);
        }

        internal bool IsSharedPtrTypeCore(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string compactName = current is CppClass cppClass
                ? cppClass.FullName.Replace(" ", string.Empty, StringComparison.Ordinal)
                : current.GetDisplayName().Replace(" ", string.Empty, StringComparison.Ordinal);
            return SharedPtrTypes.Any(candidate => compactName.StartsWith(
                candidate.Replace(" ", string.Empty, StringComparison.Ordinal) + "<", StringComparison.Ordinal));
        }

        /// <summary>
        /// Returns the stable C holder name for a shared pointer specialization.
        /// </summary>
        /// <param name="type">Configured shared pointer type.</param>
        /// <returns>A C identifier representing the shared control-block holder.</returns>
        public string GetSharedPtrHolderName(CppType type)
        {
            if (!TryGetTemplateElementType(type, out CppType? elementType))
                throw new NotSupportedException($"Unable to resolve shared_ptr element type '{type}'.");
            return NamePrefix + "SharedPtr_" + SanitizeCIdentifier(GetCType(elementType!));
        }

        /// <summary>
        /// Determines whether a C++ type uses the configured contiguous-view adapter.
        /// </summary>
        /// <param name="type">C++ type to inspect.</param>
        /// <returns><see langword="true"/> when the type is a configured span specialization.</returns>
        public bool IsSpanType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeAdapterKind.Span)) return true;
            return IsSpanTypeCore(type);
        }

        internal bool IsSpanTypeCore(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string compactName = current is CppClass cppClass
                ? cppClass.FullName.Replace(" ", string.Empty, StringComparison.Ordinal)
                : current.GetDisplayName().Replace(" ", string.Empty, StringComparison.Ordinal);
            return SpanTypes.Any(candidate => compactName.StartsWith(
                candidate.Replace(" ", string.Empty, StringComparison.Ordinal) + "<", StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether a C++ type uses the configured contiguous-container adapter.
        /// </summary>
        /// <param name="type">C++ type to inspect.</param>
        /// <returns><see langword="true"/> when the type is a configured vector specialization.</returns>
        public bool IsVectorType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeAdapterKind.Vector)) return true;
            return IsVectorTypeCore(type);
        }

        internal bool IsVectorTypeCore(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string compactName = current is CppClass cppClass
                ? cppClass.FullName.Replace(" ", string.Empty, StringComparison.Ordinal)
                : current.GetDisplayName().Replace(" ", string.Empty, StringComparison.Ordinal);
            return VectorTypes.Any(candidate => compactName.StartsWith(
                candidate.Replace(" ", string.Empty, StringComparison.Ordinal) + "<", StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether a C++ type uses the configured optional-value adapter.
        /// </summary>
        /// <param name="type">C++ type to inspect.</param>
        /// <returns><see langword="true"/> when the type is a configured optional specialization.</returns>
        public bool IsOptionalType(CppType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (ResolveCustomKind(type, CppTypeAdapterKind.Optional)) return true;
            return IsOptionalTypeCore(type);
        }

        internal bool IsOptionalTypeCore(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string compactName = current is CppClass cppClass
                ? cppClass.FullName.Replace(" ", string.Empty, StringComparison.Ordinal)
                : current.GetDisplayName().Replace(" ", string.Empty, StringComparison.Ordinal);
            return OptionalTypes.Any(candidate => compactName.StartsWith(
                candidate.Replace(" ", string.Empty, StringComparison.Ordinal) + "<", StringComparison.Ordinal));
        }

        /// <summary>Determines whether a type is a configured fixed-size array specialization.</summary>
        public bool IsArrayType(CppType type) => ResolveCustomKind(type, CppTypeAdapterKind.Array) || IsArrayTypeCore(type);
        internal bool IsArrayTypeCore(CppType type) => IsConfiguredType(type, ArrayTypes, requireTemplate: true);

        /// <summary>Determines whether a type is a configured map specialization.</summary>
        public bool IsMapType(CppType type) => ResolveCustomKind(type, CppTypeAdapterKind.Map) || IsMapTypeCore(type);
        internal bool IsMapTypeCore(CppType type) => IsConfiguredType(type, MapTypes, requireTemplate: true);

        /// <summary>Determines whether a type is a configured set specialization.</summary>
        public bool IsSetType(CppType type) => ResolveCustomKind(type, CppTypeAdapterKind.Set) || IsSetTypeCore(type);
        internal bool IsSetTypeCore(CppType type) => IsConfiguredType(type, SetTypes, requireTemplate: true);

        /// <summary>Determines whether a type is a configured variant specialization.</summary>
        public bool IsVariantType(CppType type) => ResolveCustomKind(type, CppTypeAdapterKind.Variant) || IsVariantTypeCore(type);
        internal bool IsVariantTypeCore(CppType type) => IsConfiguredType(type, VariantTypes, requireTemplate: true);

        /// <summary>Determines whether a type is a configured expected specialization.</summary>
        public bool IsExpectedType(CppType type) => ResolveCustomKind(type, CppTypeAdapterKind.Expected) || IsExpectedTypeCore(type);
        internal bool IsExpectedTypeCore(CppType type) => IsConfiguredType(type, ExpectedTypes, requireTemplate: true);

        /// <summary>Determines whether a type is a configured filesystem path.</summary>
        public bool IsPathType(CppType type) => ResolveCustomKind(type, CppTypeAdapterKind.Path) || IsPathTypeCore(type);
        internal bool IsPathTypeCore(CppType type) => IsConfiguredType(type, PathTypes, requireTemplate: false);

        /// <summary>Determines whether a type is a configured chrono duration.</summary>
        public bool IsChronoDurationType(CppType type) => ResolveCustomKind(type, CppTypeAdapterKind.ChronoDuration) || IsChronoDurationTypeCore(type);
        internal bool IsChronoDurationTypeCore(CppType type) => IsConfiguredType(type, ChronoDurationTypes, requireTemplate: true);

        /// <summary>Determines whether a type is a configured chrono time point.</summary>
        public bool IsChronoTimePointType(CppType type) => ResolveCustomKind(type, CppTypeAdapterKind.ChronoTimePoint) || IsChronoTimePointTypeCore(type);
        internal bool IsChronoTimePointTypeCore(CppType type) => IsConfiguredType(type, ChronoTimePointTypes, requireTemplate: true);

        /// <summary>Returns all type-valued arguments of a specialized template in declaration order.</summary>
        public IReadOnlyList<CppType> GetTemplateTypeArguments(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            while (current is CppTypedef typedef)
                current = UnwrapReferenceAndQualification(typedef.ElementType);
            CppType[] modeled = current is CppClass cppClass
                ? cppClass.TemplateSpecializedArguments.Where(argument => argument.ArgAsType != null)
                    .Select(argument => argument.ArgAsType!).ToArray()
                : [];
            if (modeled.Length > 0 && modeled.All(argument => argument is not CppTemplateArgument))
                return modeled;
            if (!sourceTypeSpellings.TryGetValue(type, out string? spelling))
                return modeled;
            return SplitTemplateArguments(NormalizeValueTypeSpelling(spelling))
                .Select(TryCreatePrimitiveType)
                .Where(argument => argument != null)
                .Cast<CppType>()
                .ToArray();
        }

        /// <summary>Gets the fixed element count of a configured <c>std::array</c> specialization.</summary>
        public long GetArrayElementCount(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            if (current is CppClass cppClass)
            {
                CppTemplateArgument? count = cppClass.TemplateSpecializedArguments
                    .FirstOrDefault(argument => argument.ArgKind == CppTemplateArgumentKind.AsInteger);
                if (count != null && count.ArgAsInteger >= 0)
                    return count.ArgAsInteger;
            }
            if (sourceTypeSpellings.TryGetValue(type, out string? spelling))
            {
                string[] arguments = SplitTemplateArguments(NormalizeValueTypeSpelling(spelling));
                if (arguments.Length > 1 && long.TryParse(arguments[1], out long parsed) && parsed >= 0)
                    return parsed;
            }
            throw new NotSupportedException($"Unable to resolve fixed array extent for '{type}'.");
        }

        /// <summary>Returns the stable C holder identifier for a non-contiguous C++ value specialization.</summary>
        public string GetOpaqueValueHolderName(CppType type)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            string displayName = sourceTypeSpellings.TryGetValue(type, out string? spelling)
                ? NormalizeValueTypeSpelling(spelling)
                : current is CppClass cppClass ? cppClass.FullName : current.GetDisplayName();
            return NamePrefix + "Value_" + SanitizeCIdentifier(displayName);
        }

        internal void ValidateOpaqueAdapterArguments(CppType type, CppTypeAdapterKind kind)
        {
            IReadOnlyList<CppType> arguments = GetTemplateTypeArguments(type);
            int required = kind is CppTypeAdapterKind.Map or CppTypeAdapterKind.Expected ? 2 : 1;
            if (arguments.Count < required)
                throw new NotSupportedException($"Adapter '{kind}' cannot resolve the required template arguments for '{type}'.");
            int count = kind switch
            {
                CppTypeAdapterKind.Map or CppTypeAdapterKind.Expected => 2,
                CppTypeAdapterKind.Set => 1,
                _ => arguments.Count
            };
            for (int index = 0; index < count; index++)
            {
                if (arguments[index] is CppPrimitiveType { Kind: CppPrimitiveKind.Void } ||
                    !IsBlittableBridgeType(arguments[index]))
                    throw new NotSupportedException($"Adapter '{kind}' requires ABI-value template arguments; argument {index} of '{type}' is '{arguments[index]}'. Register a custom adapter with an explicit ownership protocol.");
            }
        }

        private bool IsConfiguredType(CppType type, IEnumerable<string> configuredNames, bool requireTemplate)
        {
            if (sourceTypeSpellings.TryGetValue(type, out string? sourceSpelling) &&
                MatchesConfiguredName(NormalizeValueTypeSpelling(sourceSpelling), configuredNames, requireTemplate))
                return true;
            CppType current = UnwrapReferenceAndQualification(type);
            while (true)
            {
                string name = current switch
                {
                    CppClass cppClass => cppClass.FullName,
                    CppTypedef typedef when !string.IsNullOrEmpty(typedef.FullParentName) => typedef.FullParentName + "::" + typedef.Name,
                    _ => current.GetDisplayName()
                };
                if (MatchesConfiguredName(name, configuredNames, requireTemplate))
                    return true;
                if (current is not CppTypedef alias)
                    return false;
                current = UnwrapReferenceAndQualification(alias.ElementType);
            }
        }

        private static bool MatchesConfiguredName(string name, IEnumerable<string> configuredNames, bool requireTemplate)
        {
            string compactName = name.Replace(" ", string.Empty, StringComparison.Ordinal);
            return configuredNames.Any(candidate =>
            {
                string configured = candidate.Replace(" ", string.Empty, StringComparison.Ordinal);
                return requireTemplate
                    ? compactName.StartsWith(configured + "<", StringComparison.Ordinal)
                    : string.Equals(compactName, configured, StringComparison.Ordinal) ||
                        compactName.StartsWith(configured + "<", StringComparison.Ordinal);
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
                if (body[index] == '<') depth++;
                else if (body[index] == '>') depth--;
                else if (body[index] == ',' && depth == 0)
                {
                    arguments.Add(body[start..index].Trim());
                    start = index + 1;
                }
            }
            arguments.Add(body[start..].Trim());
            return arguments.ToArray();
        }

        private static CppType? TryCreatePrimitiveType(string spelling)
        {
            string type = spelling.Replace("const", string.Empty, StringComparison.Ordinal).Trim();
            int pointerDepth = 0;
            while (type.EndsWith('*'))
            {
                pointerDepth++;
                type = type[..^1].TrimEnd();
            }
            CppType? result = type switch
            {
                "bool" => CppPrimitiveType.Bool,
                "char" or "signed char" => CppPrimitiveType.Char,
                "unsigned char" => CppPrimitiveType.UnsignedChar,
                "short" or "short int" => CppPrimitiveType.Short,
                "unsigned short" or "unsigned short int" => CppPrimitiveType.UnsignedShort,
                "int" or "signed int" => CppPrimitiveType.Int,
                "unsigned" or "unsigned int" => CppPrimitiveType.UnsignedInt,
                "long" or "long int" => CppPrimitiveType.Long,
                "unsigned long" or "unsigned long int" => CppPrimitiveType.UnsignedLong,
                "long long" or "long long int" => CppPrimitiveType.LongLong,
                "unsigned long long" or "unsigned long long int" => CppPrimitiveType.UnsignedLongLong,
                "float" => CppPrimitiveType.Float,
                "double" => CppPrimitiveType.Double,
                "long double" => CppPrimitiveType.LongDouble,
                _ => null
            };
            while (result != null && pointerDepth-- > 0)
                result = new CppPointerType(default, result);
            return result;
        }

        private bool ResolveCustomKind(CppType type, CppTypeAdapterKind kind) =>
            Adapters.TryResolve(type, new(this, CppTypeAdapterUse.Field), out CppTypeAdapterPlan? plan) && plan!.Kind == kind;

        /// <summary>
        /// Attempts to resolve the first type argument of a configured smart pointer, view, or optional type.
        /// </summary>
        /// <param name="type">Smart pointer type.</param>
        /// <param name="elementType">Receives the first type argument.</param>
        /// <returns><see langword="true"/> when a type argument is available.</returns>
        public bool TryGetTemplateElementType(CppType type, out CppType? elementType)
        {
            CppType current = UnwrapReferenceAndQualification(type);
            if (current is CppClass { TemplateSpecializedArguments.Count: > 0 } cppClass &&
                cppClass.TemplateSpecializedArguments[0].ArgAsType is CppType argumentType)
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
                current = ((CppTypeWithElementType)current).ElementType;
            return current;
        }

        private static bool IsFunctionPointerTypedef(CppTypedef typedef) => IsFunctionPointerType(typedef.ElementType);

        /// <summary>
        /// Determines whether a native type represents a function pointer, including typedef chains.
        /// </summary>
        /// <param name="type">Type to inspect.</param>
        /// <returns><see langword="true"/> for function-pointer shapes.</returns>
        public static bool IsFunctionPointerType(CppType type)
        {
            CppType current = type;
            while (current is CppTypedef nested)
                current = nested.ElementType;
            return current is CppFunctionType || current is CppPointerType { ElementType: CppFunctionType };
        }

        /// <summary>
        /// Determines whether a type can cross the generated C ABI by value without a lifetime protocol.
        /// </summary>
        /// <param name="type">Type to inspect.</param>
        /// <returns><see langword="true"/> for primitive, enum, and pointer-shaped values.</returns>
        public bool IsBlittableBridgeType(CppType type)
        {
            return type switch
            {
                CppPrimitiveType or CppEnum or CppPointerType or CppReferenceType or CppFunctionType => true,
                CppQualifiedType qualified => IsBlittableBridgeType(qualified.ElementType),
                CppTypedef typedef => IsBlittableBridgeType(typedef.ElementType),
                _ => false
            };
        }

        private string GetQualifiedCType(CppQualifiedType qualifiedType)
        {
            string qualifier = qualifiedType.Qualifier switch
            {
                CppTypeQualifier.Const => "const ",
                CppTypeQualifier.Volatile => "volatile ",
                _ => string.Empty
            };
            return qualifier + GetCType(qualifiedType.ElementType);
        }

        private static string GetPrimitiveName(CppPrimitiveType primitiveType)
        {
            return primitiveType.Kind switch
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
        /// Performs the operation implemented by <c>GetCTypeName</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetCTypeName</c>.</returns>
        public string GetCTypeName(CppClass c)
        {
            string name = c.TemplateKind == CppTemplateKind.TemplateSpecializedClass
                ? SanitizeCIdentifier(c.FullName)
                : c.Name;
            return NamePrefix + name;
        }

        /// <summary>
        /// Returns the stable C identifier generated for a C++ free function.
        /// </summary>
        /// <param name="cppFunction">Source function declaration.</param>
        /// <returns>A namespace-qualified and prefix-qualified C identifier.</returns>
        public string GetCFunctionName(CppFunction cppFunction)
        {
            string parent = cppFunction.FullParentName.Replace("::", "_", StringComparison.Ordinal);
            string name = string.IsNullOrEmpty(parent) ? cppFunction.Name : parent + "_" + cppFunction.Name;
            string defaultName = NamePrefix + SanitizeCIdentifier(name);
            return GetCFunctionName(null, cppFunction, defaultName);
        }

        /// <summary>
        /// Returns the stable C identifier generated for a C++ enum.
        /// </summary>
        /// <param name="cppEnum">Source enum declaration.</param>
        /// <returns>A namespace-qualified and prefix-qualified C identifier.</returns>
        public string GetCTypeName(CppEnum cppEnum)
        {
            string parent = cppEnum.FullParentName.Replace("::", "_", StringComparison.Ordinal);
            string name = string.IsNullOrEmpty(parent) ? cppEnum.Name : parent + "_" + cppEnum.Name;
            return NamePrefix + SanitizeCIdentifier(name);
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
