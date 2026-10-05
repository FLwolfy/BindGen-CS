using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BGCS.Analysis;
using BGCS.Configuration;

namespace BGCS.Conversion
{
    using System.Text;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Interfaces;
    using BGCS.CppAst.Model.Templates;
    using BGCS.CppAst.Model.Types;
    using BGCS.Platform;

    /// <summary>
    /// Selects an ABI carrier, managed reference projection, or runtime pointer wrapper during type conversion.
    /// </summary>
    public enum CsTypeStyle
    {
        /// <summary>
        /// Native ABI carrier without managed reference projections.
        /// </summary>
        Raw,
        /// <summary>
        /// Managed reference projection used by generated convenience overloads.
        /// </summary>
        Ref,
        /// <summary>
        /// Runtime pointer or handle wrapper projection.
        /// </summary>
        Wrapped,
    }

    /// <summary>
    /// Converts borrowed native types according to generation policy and retains only attempt-local AST caches.
    /// </summary>
    public class CppTypeConverter
    {
        private readonly CsCodeGeneratorConfig m_config;
        private readonly Dictionary<CppType, AnalysisResult> m_typedefCache = [];
        private readonly Lock m_syncObj = new();
        private Dictionary<string, CppEnum> m_typeDefToEnum = [];
        private Dictionary<CppType, string> m_anonymousMapping = [];
        /// <summary>
        /// Retains the mutable target, naming, and interop policy used by conversion.
        /// </summary>
        /// <param name="config">
        /// The configuration borrowed for this converter's lifetime.
        /// </param>
        public CppTypeConverter(CsCodeGeneratorConfig config)
        {
            this.m_config = config;
        }

        /// <summary>
        /// Clears previous AST caches and discovers enum/typedef relationships in the current compilation.
        /// </summary>
        /// <param name="result">
        /// The attempt-local model whose compilation remains alive until conversion completes.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The analysis result is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The compilation contains duplicate enum names after configured enum-name preprocessing.
        /// </exception>
        public void Initialize(ParseResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            this.m_typedefCache.Clear();
            this.m_anonymousMapping.Clear();
            var compilation = result.compilation;
            this.m_typeDefToEnum = compilation.enums.ToDictionary(e => PreprocessEnumName(e.name));
            var enumMap = compilation.enums.ToDictionary(e => e.name);
            foreach (var pair in this.m_config.typedefToEnumMappings)
            {
                if (pair.Value == null)
                {
                    this.m_typeDefToEnum.Remove(pair.Key);
                }
                else if (enumMap.TryGetValue(pair.Value, out var cppEnum))
                {
                    this.m_typeDefToEnum[pair.Key] = cppEnum;
                }
            }
        }

        internal void ReleaseCompilation()
        {
            this.m_typedefCache.Clear();
            this.m_anonymousMapping.Clear();
            this.m_typeDefToEnum.Clear();
        }

        /// <summary>
        /// Registers the managed name for an anonymous native type in the current attempt.
        /// </summary>
        /// <param name="anon">
        /// The borrowed anonymous type node used as the exact cache key.
        /// </param>
        /// <param name="name">
        /// The managed identifier replacing inferred anonymous-type naming.
        /// </param>
        public void AddAnonymousMapping(
            CppType anon,
            string name
        ) {
            this.m_anonymousMapping[anon] = name;
        }

        private static string PreprocessEnumName(ReadOnlySpan<char> name)
        {
            if (name.EndsWith("_t"))
            {
                name = name[..^2];
            }
            else if (name.EndsWith('_'))
            {
                name = name[..^1];
            }

            return name.ToString();
        }

        private struct AnalysisResult
        {
            /// <summary>
            /// Exposes public member <c>BaseType</c>.
            /// </summary>
            public string baseType;
            /// <summary>
            /// Exposes public member <c>PointerLevel</c>.
            /// </summary>
            public int pointerLevel;
            /// <summary>
            /// Exposes public member <c>IsConst</c>.
            /// </summary>
            public bool isConst;
            /// <summary>
            /// Exposes public member <c>Function</c>.
            /// </summary>
            public CppFunctionType? function;
            /// <summary>
            /// Exposes public member <c>null</c>.
            /// </summary>
            public readonly bool isFunctionPointer => this.pointerLevel == 1 && this.function != null;

            /// <summary>
            /// Merges configuration or metadata via <c>Merge</c>.
            /// </summary>
            public void Merge(in AnalysisResult result)
            {
                this.baseType = result.baseType;
                this.pointerLevel += result.pointerLevel;
                this.isConst |= result.isConst && this.pointerLevel == 1;
                this.function = result.function;
            }
        }

        /// <summary>
        /// Resolves a target ABI carrier or formats the recursively analyzed type in the requested projection style.
        /// </summary>
        /// <param name="type">
        /// The borrowed native type to convert while its compilation remains alive.
        /// </param>
        /// <param name="style">
        /// The raw, reference, or wrapped managed projection to produce.
        /// </param>
        /// <returns>
        /// A C# type spelling suitable for the selected projection, with pointer levels and qualifiers lowered according to policy.
        /// </returns>
        /// <exception cref="UnexposedTypeException">
        /// An unexposed native type cannot be represented by the configured conversion policy.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// The projection style or encountered native type kind is unsupported.
        /// </exception>
        public string Convert(
            CppType type,
            CsTypeStyle style
        ) {
            if (PlatformAbiTypeClassifier.TryGetManagedCarrier(type, out string managedType))
            {
                return managedType;
            }

            var result = AnalyzeType(type);
            return Format(result, style);
        }

        private string Format(
            in AnalysisResult result,
            CsTypeStyle style
        ) {
            return style switch
            {
                CsTypeStyle.Raw => FormatRaw(result),
                CsTypeStyle.Ref => FormatRef(result),
                CsTypeStyle.Wrapped => FormatWrapped(result),
                _ => throw new NotSupportedException(),
            };
        }

        private string FormatRaw(AnalysisResult result)
        {
            if (result.function != null)
            {
                if (this.m_config.delegatesAsVoidPointer)
                {
                    result.baseType = "void";
                }
                else
                {
                    result.baseType = this.m_config.MakeDelegatePointer(result.function);
                    --result.pointerLevel;
                }
            }

            return result.baseType + new string('*', result.pointerLevel);
        }

        private string FormatRef(AnalysisResult result)
        {
            if (result.baseType == "void" && result.pointerLevel > 0)
            {
                result.baseType = "nint";
                --result.pointerLevel;
            }

            if (result.function != null)
            {
                if (result.pointerLevel > 1 || string.IsNullOrWhiteSpace(result.baseType))
                {
                    return FormatRaw(result);
                }

                --result.pointerLevel;
            }

            StringBuilder sb = new();
            if (result.pointerLevel > 0)
            {
                sb.Append(result.isConst ? "in " : "ref ");
                --result.pointerLevel;
            }

            sb.Append(result.baseType);
            for (int i = 0; i < result.pointerLevel; i++)
            {
                sb.Append('*');
            }

            return sb.ToString();
        }

        private string FormatWrapped(AnalysisResult result)
        {
            if (result.baseType == "void" && result.pointerLevel > 0)
            {
                result.baseType = "nint";
                --result.pointerLevel;
            }

            if (result.function != null)
            {
                if (result.pointerLevel > 1 || string.IsNullOrWhiteSpace(result.baseType))
                {
                    return FormatRaw(result);
                }

                --result.pointerLevel;
            }

            StringBuilder sb = new();
            for (int i = 0; i < result.pointerLevel; ++i)
            {
                sb.Append("Pointer<");
            }

            sb.Append(result.baseType);
            for (int i = 0; i < result.pointerLevel; ++i)
            {
                sb.Append('>');
            }

            return sb.ToString();
        }

        private AnalysisResult AnalyzeType(CppType type)
        {
            AnalysisResult result = new();
            CppType? currentType = type;
            while (currentType != null)
            {
                if (currentType is CppPointerType pointerType)
                {
                    ++result.pointerLevel;
                    currentType = pointerType.elementType;
                }
                else if (currentType is CppReferenceType referenceType)
                {
                    ++result.pointerLevel;
                    currentType = referenceType.elementType;
                }
                else if (currentType is CppQualifiedType qualifiedType)
                {
                    result.isConst |= qualifiedType.qualifier == CppTypeQualifier.Const && result.pointerLevel == 1;
                    currentType = qualifiedType.elementType;
                }
                else if (currentType is CppPrimitiveType primitiveType)
                {
                    result.baseType = ConvertPrimitiveType(primitiveType);
                    break;
                }
                else if (currentType is CppTypedef typedef)
                {
                    result.Merge(ResolveTypedef(typedef));
                    break;
                }
                else if (currentType is CppEnum cppEnum)
                {
                    result.baseType = GetMapping(cppEnum);
                    break;
                }
                else if (currentType is CppClass cppClass)
                {
                    result.baseType = GetMapping(cppClass);
                    break;
                }
                else if (currentType is CppArrayType arrayType)
                {
                    ++result.pointerLevel;
                    currentType = arrayType.elementType;
                }
                else if (currentType is CppFunctionType functionType)
                {
                    result.function = functionType;
                    break;
                }
                else if (currentType is CppTemplateArgument { argAsType: not null } templateArgument)
                {
                    currentType = templateArgument.argAsType;
                }
                else if (currentType is CppTemplateParameterNonType nonTypeParameter)
                {
                    currentType = nonTypeParameter.noneTemplateType;
                }
                else if (currentType is CppTemplateParameterType templateParameter)
                {
                    if (!this.m_config.typeMappings.TryGetValue(templateParameter.name, out string? mapping))
                    {
                        throw new NotSupportedException($"Template parameter '{templateParameter.name}' requires a concrete specialization or TypeMappings entry.");
                    }

                    result.baseType = mapping;
                    break;
                }
                else if (currentType is CppUnexposedType unexposedType)
                {
                    if (!this.m_config.typeMappings.TryGetValue(unexposedType.name, out string? mapping))
                    {
                        throw new UnexposedTypeException(unexposedType);
                    }

                    result.baseType = mapping;
                    break;
                }
                else if (currentType is CppGenericType genericType)
                {
                    string genericName = genericType.ToString();
                    if (!this.m_config.typeMappings.TryGetValue(genericName, out string? mapping))
                    {
                        throw new NotSupportedException($"Generic type '{genericName}' requires a TypeMappings entry or a generated C++ bridge specialization.");
                    }

                    result.baseType = mapping;
                    break;
                }
                else
                {
                    throw new NotSupportedException($"C++ type '{currentType}' ({currentType.typeKind}) is not supported by the C# type converter.");
                }
            }

            return result;
        }

        private AnalysisResult ResolveTypedef(CppTypedef typedef)
        {
            bool isDelegate = typedef.elementType.IsDelegate(out var delegateType);
            if (isDelegate)
            {
                if (this.m_config.delegatesAsVoidPointer)
                {
                    return new()
                    {
                        baseType = "void",
                        pointerLevel = 1
                    };
                }

                if (!this.m_config.generateDelegates)
                {
                    return new()
                    {
                        baseType = this.m_config.GetDelegatePointerType(delegateType!)
                    };
                }
            }

            if (this.m_typeDefToEnum.TryGetValue(typedef.name, out var cppEnum))
            {
                return new()
                {
                    baseType = GetMapping(cppEnum)
                };
            }

            if (this.m_config.typeMappings.TryGetValue(typedef.name, out var name))
            {
                return new()
                {
                    baseType = name
                };
            }

            lock (this.m_syncObj)
            {
                if (!this.m_typedefCache.TryGetValue(typedef, out var result))
                {
                    if (typedef.IsOpaqueHandle())
                    {
                        result = this.m_config.generateHandles ? new()
                        {
                            baseType = this.m_config.GetManagedHandleName(typedef.name)
                        }

                        : AnalyzeType(typedef.elementType);
                        this.m_typedefCache.Add(typedef, result);
                        return result;
                    }

                    result = AnalyzeType(typedef.elementType);
                    if (result.isFunctionPointer)
                    {
                        result.baseType = GetMapping(typedef);
                    }

                    this.m_typedefCache.Add(typedef, result);
                }

                return result;
            }
        }

        private string GetMapping<T>(T member)
            where T : CppType, ICppMember
        {
            if (this.m_anonymousMapping.TryGetValue(member, out var name))
            {
                return name;
            }

            if (this.m_config.typeMappings.TryGetValue(member.name, out name))
            {
                return name;
            }

            if (member is CppEnum cppEnum && this.m_config.TryGetEnumMapping(cppEnum.name, out var enumMapping) && !string.IsNullOrWhiteSpace(enumMapping.friendlyName))
            {
                return enumMapping.friendlyName;
            }

            if (member is CppClass cppClass && this.m_config.TryGetTypeMapping(cppClass.name, out var typeMapping) && !string.IsNullOrWhiteSpace(typeMapping.friendlyName))
            {
                return typeMapping.friendlyName;
            }

            return member switch
            {
                CppEnum => this.m_config.GetManagedEnumName(member.name),
                CppTypedef typedef when typedef.IsOpaqueHandle() => this.m_config.GetManagedHandleName(member.name),
                _ => this.m_config.GetManagedTypeName(member.name)
            };
        }

        private string ConvertPrimitiveType(CppPrimitiveType primitiveType)
        {
            return NativeAbi.GetPrimitiveTypeName(primitiveType);
        }
    }
}
