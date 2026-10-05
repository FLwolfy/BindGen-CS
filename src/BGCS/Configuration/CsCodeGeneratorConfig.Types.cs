using BGCS.Analysis.Constants;
using BGCS.Configuration.Naming;
using BGCS.Core.Writing;
using BGCS.CppAst.Extensions;
using BGCS.Text;

namespace BGCS.Configuration
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Text;
    using System.Xml.Linq;
    using BGCS.Configuration.Mapping;
    using BGCS.Conversion;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Model.Types;
    using Microsoft.CodeAnalysis.CSharp;

    /// <summary>
    /// Configures native-to-managed type spelling, declaration naming, annotations, and binding projection policies.
    /// </summary>
    public partial class CsCodeGeneratorConfig
    {
        #region Mapping Helpers
        /// <summary>
        /// Finds the first configured enum mapping with an exact native identifier match.
        /// </summary>
        /// <param name="enumName">
        /// The ordinal native identifier to match.
        /// </param>
        /// <param name="mapping">
        /// Receives the configured mutable mapping object, or null when no entry matches.
        /// </param>
        /// <returns>
        /// True when a mapping is found; the returned mapping remains owned by this configuration.
        /// </returns>
        public bool TryGetEnumMapping(
            string enumName,
            [NotNullWhen(true)] out EnumMapping? mapping
        ) {
            for (int i = 0; i < this.enumMappings.Count; i++)
            {
                var enumMapping = this.enumMappings[i];
                if (enumMapping.exportedName == enumName)
                {
                    mapping = enumMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Finds the first configured enum mapping with an exact native identifier match.
        /// </summary>
        /// <param name="enumName">
        /// The ordinal native identifier to match.
        /// </param>
        /// <returns>
        /// The retained mutable mapping, or null when no entry matches.
        /// </returns>
        public EnumMapping? GetEnumMapping(string enumName)
        {
            for (int i = 0; i < this.enumMappings.Count; i++)
            {
                var enumMapping = this.enumMappings[i];
                if (enumMapping.exportedName == enumName)
                {
                    return enumMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the first configured function mapping with an exact native identifier match.
        /// </summary>
        /// <param name="functionName">
        /// The ordinal native identifier to match.
        /// </param>
        /// <param name="mapping">
        /// Receives the configured mutable mapping object, or null when no entry matches.
        /// </param>
        /// <returns>
        /// True when a mapping is found; the returned mapping remains owned by this configuration.
        /// </returns>
        public bool TryGetFunctionMapping(
            string functionName,
            [NotNullWhen(true)] out FunctionMapping? mapping
        ) {
            for (int i = 0; i < this.functionMappings.Count; i++)
            {
                var functionMapping = this.functionMappings[i];
                if (functionMapping.exportedName == functionName)
                {
                    mapping = functionMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Finds the first configured function mapping with an exact native identifier match.
        /// </summary>
        /// <param name="functionName">
        /// The ordinal native identifier to match.
        /// </param>
        /// <returns>
        /// The retained mutable mapping, or null when no entry matches.
        /// </returns>
        public FunctionMapping? GetFunctionMapping(string functionName)
        {
            for (int i = 0; i < this.functionMappings.Count; i++)
            {
                var functionMapping = this.functionMappings[i];
                if (functionMapping.exportedName == functionName)
                {
                    return functionMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the first configured class or struct mapping with an exact native identifier match.
        /// </summary>
        /// <param name="typeName">
        /// The ordinal native identifier to match.
        /// </param>
        /// <param name="mapping">
        /// Receives the configured mutable mapping object, or null when no entry matches.
        /// </param>
        /// <returns>
        /// True when a mapping is found; the returned mapping remains owned by this configuration.
        /// </returns>
        public bool TryGetTypeMapping(
            string typeName,
            [NotNullWhen(true)] out TypeMapping? mapping
        ) {
            for (int i = 0; i < this.classMappings.Count; i++)
            {
                var structMapping = this.classMappings[i];
                if (structMapping.exportedName == typeName)
                {
                    mapping = structMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Finds the first configured class or struct mapping with an exact native identifier match.
        /// </summary>
        /// <param name="typeName">
        /// The ordinal native identifier to match.
        /// </param>
        /// <returns>
        /// The retained mutable mapping, or null when no entry matches.
        /// </returns>
        public TypeMapping? GetTypeMapping(string typeName)
        {
            for (int i = 0; i < this.classMappings.Count; i++)
            {
                var structMapping = this.classMappings[i];
                if (structMapping.exportedName == typeName)
                {
                    return structMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the first configured handle mapping with an exact native identifier match.
        /// </summary>
        /// <param name="typeName">
        /// The ordinal native identifier to match.
        /// </param>
        /// <param name="mapping">
        /// Receives the configured mutable mapping object, or null when no entry matches.
        /// </param>
        /// <returns>
        /// True when a mapping is found; the returned mapping remains owned by this configuration.
        /// </returns>
        public bool TryGetHandleMapping(
            string typeName,
            [NotNullWhen(true)] out HandleMapping? mapping
        ) {
            for (int i = 0; i < this.handleMappings.Count; i++)
            {
                var handleMapping = this.handleMappings[i];
                if (handleMapping.exportedName == typeName)
                {
                    mapping = handleMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Finds the first configured handle mapping with an exact native identifier match.
        /// </summary>
        /// <param name="typeName">
        /// The ordinal native identifier to match.
        /// </param>
        /// <returns>
        /// The retained mutable mapping, or null when no entry matches.
        /// </returns>
        public HandleMapping? GetHandleMapping(string typeName)
        {
            for (int i = 0; i < this.handleMappings.Count; i++)
            {
                var handleMapping = this.handleMappings[i];
                if (handleMapping.exportedName == typeName)
                {
                    return handleMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the first configured callback mapping with an exact native identifier match.
        /// </summary>
        /// <param name="delegateName">
        /// The ordinal native identifier to match.
        /// </param>
        /// <param name="mapping">
        /// Receives the configured mutable mapping object, or null when no entry matches.
        /// </param>
        /// <returns>
        /// True when a mapping is found; the returned mapping remains owned by this configuration.
        /// </returns>
        public bool TryGetDelegateMapping(
            string delegateName,
            [NotNullWhen(true)] out DelegateMapping? mapping
        ) {
            for (int i = 0; i < this.delegateMappings.Count; i++)
            {
                var delegateMapping = this.delegateMappings[i];
                if (delegateMapping.name == delegateName)
                {
                    mapping = delegateMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Finds the first configured callback mapping with an exact native identifier match.
        /// </summary>
        /// <param name="delegateName">
        /// The ordinal native identifier to match.
        /// </param>
        /// <returns>
        /// The retained mutable mapping, or null when no entry matches.
        /// </returns>
        public DelegateMapping? GetDelegateMapping(string delegateName)
        {
            for (int i = 0; i < this.delegateMappings.Count; i++)
            {
                var delegateMapping = this.delegateMappings[i];
                if (delegateMapping.name == delegateName)
                {
                    return delegateMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a configured fixed array projection by primitive element kind and extent.
        /// </summary>
        /// <param name="arrayType">
        /// The native array descriptor to classify.
        /// </param>
        /// <param name="mapping">
        /// Receives the configured managed type name, or null when no shape matches.
        /// </param>
        /// <returns>
        /// True when both the primitive element kind and fixed element count match a mapping.
        /// </returns>
        public bool TryGetArrayMapping(
            CppArrayType arrayType,
            [NotNullWhen(true)] out string? mapping
        ) {
            for (int i = 0; i < this.arrayMappings.Count; i++)
            {
                var map = this.arrayMappings[i];
                if (map.primitive == arrayType.GetPrimitiveKind(false) && map.size == arrayType.size)
                {
                    mapping = map.name;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        #endregion Mapping Helpers
        /// <summary>
        /// Resolves the managed return spelling, projecting native function signatures according to the callback policy.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor from the current compilation, or null for an absent type.
        /// </param>
        /// <returns>
        /// The configured raw type or callback spelling; an empty string for a null type.
        /// </returns>
        public string GetCsReturnType(CppType? type)
        {
            if (type == null)
                return string.Empty;
            if (type.IsDelegate(out var outDelegate))
            {
                return GetDelegatePointerType(outDelegate);
            }

            var name = GetCsTypeNameInternal(type);
            return name;
        }

        /// <summary>
        /// Resolves the configured raw managed spelling of a native type.
        /// </summary>
        /// <param name="type">
        /// The native type descriptor from the current compilation, or null for an absent type.
        /// </param>
        /// <returns>
        /// The raw native-compatible managed type spelling; an empty string for a null type.
        /// </returns>
        public string GetCsTypeName(CppType? type)
        {
            if (type == null)
                return string.Empty;
            var name = GetCsTypeNameInternal(type);
            return name;
        }

        /// <summary>
        /// Constructs a C# function-pointer signature with native Boolean carriers and the selected calling convention.
        /// </summary>
        /// <param name="functionType">
        /// The native callback signature, including its parameters and return type.
        /// </param>
        /// <param name="withConvention">
        /// Whether to emit an unmanaged convention marker rather than an unqualified function pointer.
        /// </param>
        /// <returns>
        /// The delegate-star type spelling for the complete native signature.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// The callback calling convention has no supported managed projection.
        /// </exception>
        public string MakeDelegatePointer(
            CppFunctionType functionType,
            bool withConvention = true
        ) {
            string returnType = GetCsTypeNameInternal(functionType.returnType);
            if (returnType == "bool")
            {
                returnType = GetBoolType();
            }

            if (withConvention)
            {
                if (functionType.parameters.Count == 0)
                {
                    return $"delegate* unmanaged[{functionType.callingConvention.GetCallingConventionDelegate()}]<{returnType}>";
                }
                else
                {
                    return $"delegate* unmanaged[{functionType.callingConvention.GetCallingConventionDelegate()}]<{GetNamelessParameterSignature(functionType.parameters, false, true)}, {returnType}>";
                }
            }
            else
            {
                if (functionType.parameters.Count == 0)
                {
                    return $"delegate*<{returnType}>";
                }
                else
                {
                    return $"delegate*<{GetNamelessParameterSignature(functionType.parameters, false, true)}, {returnType}>";
                }
            }
        }

        /// <summary>
        /// Applies the configured opaque-pointer policy or constructs a typed callback pointer.
        /// </summary>
        /// <param name="functionType">
        /// The native callback signature, including its parameters and return type.
        /// </param>
        /// <param name="withConvention">
        /// Whether to emit an unmanaged convention marker rather than an unqualified function pointer.
        /// </param>
        /// <returns>
        /// void* when callbacks are opaque; otherwise the complete function-pointer signature.
        /// </returns>
        public string GetDelegatePointerType(
            CppFunctionType functionType,
            bool withConvention = true
        ) {
            if (this.delegatesAsVoidPointer)
            {
                return "void*";
            }

            return MakeDelegatePointer(functionType, withConvention);
        }

        private string GetCsTypeNameInternal(CppType type)
        {
            return this.m_converter.Convert(type, CsTypeStyle.Raw);
        }

        /// <summary>
        /// Resolves a managed reference projection instead of a raw native pointer spelling.
        /// </summary>
        /// <param name="type">Native type from a live compilation, or null for an absent type.</param>
        /// <returns>The projected type spelling, or an empty string when the input type is null.</returns>
        public string GetCsWrapperTypeName(CppType? type)
        {
            if (type == null)
                return string.Empty;
            var name = this.m_converter.Convert(type, CsTypeStyle.Ref);
            return name;
        }

        /// <summary>
        /// Resolves a runtime pointer wrapper instead of a raw native pointer spelling.
        /// </summary>
        /// <param name="type">Native type from a live compilation, or null for an absent type.</param>
        /// <returns>The wrapper type spelling, or an empty string when the input type is null.</returns>
        public string GetCsWrappedPointerTypeName(CppType? type)
        {
            if (type == null)
                return string.Empty;
            var name = this.m_converter.Convert(type, CsTypeStyle.Wrapped);
            return name;
        }

        /// <summary>
        /// Formats a managed parameter declaration list under the current mapping, Boolean, callback, and metadata policies.
        /// </summary>
        /// <param name="parameters">
        /// The native parameter descriptors in declaration order.
        /// </param>
        /// <param name="canUseOut">
        /// Whether supported direct pointer targets may be expressed as managed out parameters.
        /// </param>
        /// <param name="attributes">
        /// Whether to include native-name attributes when metadata emission is enabled.
        /// </param>
        /// <param name="names">
        /// Whether to include the normalized managed parameter names.
        /// </param>
        /// <param name="delegateType">
        /// Whether callback typedefs should expand into typed function pointers.
        /// </param>
        /// <returns>
        /// Comma-separated parameter declarations without surrounding parentheses; empty for a parameterless signature.
        /// </returns>
        public string GetParameterSignature(
            IList<CppParameter> parameters,
            bool canUseOut,
            bool attributes = true,
            bool names = true,
            bool delegateType = false
        ) {
            StringBuilder argumentBuilder = new();
            int index = 0;
            for (int i = 0; i < parameters.Count; i++)
            {
                CppParameter cppParameter = parameters[i];
                var paramCsTypeName = GetCsTypeName(cppParameter.type);
                var paramCsName = GetParameterName(i, cppParameter.name);
                CppType ptrType = cppParameter.type;
                int depth = 0;
                if (cppParameter.type.IsPointer(ref depth, out var pointerType))
                {
                    ptrType = pointerType;
                }

                if ((delegateType || ptrType != cppParameter.type) && ptrType is CppTypedef typedef && typedef.elementType.IsDelegate(out var cppFunction) && !paramCsTypeName.Contains('*'))
                {
                    paramCsTypeName = GetDelegatePointerType(cppFunction);
                    while (depth-- > 0)
                    {
                        paramCsTypeName += "*";
                    }
                }

                if (attributes && this.generateMetadata)
                {
                    argumentBuilder.Append($"[NativeName(NativeNameType.Param, \"{cppParameter.name}\")] ");
                    argumentBuilder.Append($"[NativeName(NativeNameType.Type, \"{cppParameter.type.GetDisplayName()}\")] ");
                }

                if (paramCsTypeName == "bool")
                {
                    paramCsTypeName = GetBoolType();
                }

                if (canUseOut && cppParameter.type.CanBeUsedAsOutput(out CppTypeDeclaration? cppTypeDeclaration))
                {
                    argumentBuilder.Append("out ");
                    paramCsTypeName = GetCsTypeName(cppTypeDeclaration);
                }

                argumentBuilder.Append(paramCsTypeName);
                if (names)
                {
                    argumentBuilder.Append(' ').Append(paramCsName);
                }

                if (index < parameters.Count - 1)
                {
                    argumentBuilder.Append(", ");
                }

                index++;
            }

            return argumentBuilder.ToString();
        }

        /// <summary>
        /// Formats normalized parameter names as a managed invocation argument list.
        /// </summary>
        /// <param name="parameters">
        /// The native parameter descriptors in declaration order.
        /// </param>
        /// <returns>
        /// Comma-separated managed argument names without surrounding parentheses; empty for no parameters.
        /// </returns>
        public string GetParameterSignatureNames(IList<CppParameter> parameters)
        {
            StringBuilder argumentBuilder = new();
            int index = 0;
            for (int i = 0; i < parameters.Count; i++)
            {
                CppParameter cppParameter = parameters[i];
                var paramCsTypeName = GetCsTypeName(cppParameter.type);
                var paramCsName = GetParameterName(i, cppParameter.name);
                CppType ptrType = cppParameter.type;
                int depth = 0;
                if (cppParameter.type.IsPointer(ref depth, out var pointerType))
                {
                    ptrType = pointerType;
                }

                argumentBuilder.Append(paramCsName);
                if (index < parameters.Count - 1)
                {
                    argumentBuilder.Append(", ");
                }

                index++;
            }

            return argumentBuilder.ToString();
        }

        /// <summary>
        /// Formats managed parameter type spellings without parameter identifiers or metadata attributes.
        /// </summary>
        /// <param name="parameters">
        /// The native parameter descriptors in declaration order.
        /// </param>
        /// <param name="canUseOut">
        /// Whether supported direct pointer targets may be expressed as managed out parameters.
        /// </param>
        /// <param name="delegateType">
        /// Whether callback typedefs should expand into typed function pointers.
        /// </param>
        /// <returns>
        /// Comma-separated type spellings, optionally including out modifiers; empty for no parameters.
        /// </returns>
        public string GetNamelessParameterSignature(
            IList<CppParameter> parameters,
            bool canUseOut,
            bool delegateType = false
        ) {
            var argumentBuilder = new StringBuilder();
            int index = 0;
            foreach (CppParameter cppParameter in parameters)
            {
                string direction = string.Empty;
                var paramCsTypeName = GetCsTypeName(cppParameter.type);
                CppType ptrType = cppParameter.type;
                int depth = 0;
                if (cppParameter.type.IsPointer(ref depth, out var pointerType))
                {
                    ptrType = pointerType;
                }

                if (cppParameter.type is CppQualifiedType qualifiedType)
                {
                    ptrType = qualifiedType.elementType;
                }

                if (delegateType && ptrType is CppTypedef typedef && typedef.elementType.IsDelegate(out var cppFunction))
                {
                    paramCsTypeName = GetDelegatePointerType(cppFunction);
                    while (depth-- > 0)
                    {
                        paramCsTypeName += "*";
                    }
                }

                if (paramCsTypeName == "bool")
                {
                    paramCsTypeName = GetBoolType();
                }

                if (canUseOut && cppParameter.type.CanBeUsedAsOutput(out CppTypeDeclaration? cppTypeDeclaration))
                {
                    argumentBuilder.Append("out ");
                    paramCsTypeName = GetCsTypeName(cppTypeDeclaration);
                }

                argumentBuilder.Append(paramCsTypeName);
                if (index < parameters.Count - 1)
                {
                    argumentBuilder.Append(", ");
                }

                index++;
            }

            return argumentBuilder.ToString();
        }

        /// <summary>
        /// Formats normalized invocation argument names without adding conversion expressions.
        /// </summary>
        /// <param name="parameters">
        /// The native parameter descriptors in declaration order.
        /// </param>
        /// <returns>
        /// Comma-separated normalized names; empty for a parameterless invocation.
        /// </returns>
        public string WriteFunctionMarshalling(IList<CppParameter> parameters)
        {
            var argumentBuilder = new StringBuilder();
            int index = 0;
            for (int i = 0; i < parameters.Count; i++)
            {
                CppParameter cppParameter = parameters[i];
                var paramCsName = GetParameterName(i, cppParameter.name);
                argumentBuilder.Append(paramCsName);
                if (index < parameters.Count - 1)
                {
                    argumentBuilder.Append(", ");
                }

                index++;
            }

            return argumentBuilder.ToString();
        }

        private readonly ConcurrentDictionary<(string Name, NamingConvention Convention), string> m_parameterNameCache = new();
        /// <summary>
        /// Resolves unnamed parameters and native keyword spellings to usable managed identifiers.
        /// </summary>
        /// <param name="paramIdx">
        /// The zero-based parameter index used only for unnamed parameter identifiers.
        /// </param>
        /// <param name="name">
        /// The native parameter spelling; empty names become indexed unknown identifiers.
        /// </param>
        /// <returns>
        /// The normalized or explicitly substituted managed parameter identifier, escaped when required.
        /// </returns>
        public string GetParameterName(
            int paramIdx,
            string name
        ) {
            if (name == "out")
            {
                return "output";
            }

            if (name == "ref")
            {
                return "reference";
            }

            if (name == "in")
            {
                return "input";
            }

            if (name == "base")
            {
                return "baseValue";
            }

            if (name == "void")
            {
                return "voidValue";
            }

            if (name == "int")
            {
                return "intValue";
            }

            if (name == "lock")
            {
                return "lock0";
            }

            if (name == "event")
            {
                return "evnt";
            }

            if (name == "string")
            {
                return "str";
            }

            if (IsCSharpKeyword(name))
            {
                return "@" + name;
            }

            if (name == string.Empty)
            {
                return $"unknown{paramIdx}";
            }

            return NormalizeParameterName(name);
        }

        /// <summary>
        /// Applies the callback naming convention and configured name replacements.
        /// </summary>
        /// <param name="name">
        /// The native callback identifier to project.
        /// </param>
        /// <returns>
        /// The managed callback identifier under the current naming policy.
        /// </returns>
        public string GetDelegateName(string name)
        {
            if (this.delegateNamingConvention == NamingConvention.PascalCase)
            {
                return GetCsCleanName(name);
            }

            string newName = NamingHelper.ConvertTo(name, this.delegateNamingConvention);
            foreach (var item in this.nameMappings)
            {
                newName = newName.Replace(item.Key, item.Value, StringComparison.InvariantCultureIgnoreCase);
            }

            return newName;
        }

        /// <summary>
        /// Normalizes a parameter spelling under the current naming convention and escapes the current keyword policy.
        /// </summary>
        /// <param name="name">
        /// The native identifier; surrounding underscores are trimmed before word conversion.
        /// </param>
        /// <returns>
        /// A usable managed identifier; empty normalized names become value and leading digits receive an underscore.
        /// </returns>
        public string NormalizeParameterName(string name)
        {
            name = name.Trim('_');
            string newName = this.m_parameterNameCache.GetOrAdd(
                (name, this.parameterNamingConvention),
                static key => NamingHelper.ConvertTo(key.Name, key.Convention));
            if (string.IsNullOrEmpty(newName))
            {
                newName = "value";
            }

            if (IsCSharpKeyword(newName))
            {
                newName = "@" + newName;
            }

            if (char.IsDigit(newName[0]))
            {
                newName = "_" + newName;
            }

            return newName;
        }

        /// <summary>
        /// Resolves configured default-expression names and known native null, Boolean, and floating-boundary constants.
        /// </summary>
        /// <param name="value">
        /// The complete native default-expression spelling to inspect.
        /// </param>
        /// <returns>
        /// The mapped managed expression, a known constant projection, or the unchanged native text.
        /// </returns>
        public string NormalizeValue(string value)
        {
            if (this.knownDefaultValueNames.TryGetValue(value, out var names))
            {
                return names;
            }

            if (value == "NULL")
                return "default";
            if (value == "FLT_MAX")
                return "float.MaxValue";
            if (value == "-FLT_MAX")
                return "-float.MaxValue";
            if (value == "FLT_MIN")
                return "1.17549435E-38f";
            if (value == "-FLT_MIN")
                return "-1.17549435E-38f";
            if (value == "nullptr")
                return "default";
            if (value == "false")
                return "0";
            if (value == "true")
                return "1";
            return value;
        }

        /// <summary>
        /// Projects a native function identifier using an explicit mapping or the longest configured prefix and naming convention.
        /// </summary>
        /// <param name="function">
        /// The ordinal native function identifier to project.
        /// </param>
        /// <returns>
        /// The mapped managed name or the convention-normalized name after prefix and ignored-word removal.
        /// </returns>
        public string GetCsFunctionName(string function)
        {
            if (TryGetFunctionMapping(function, out var mapping))
            {
                if (mapping.friendlyName != null)
                    return mapping.friendlyName;
            }

            string candidate = function;
            string? prefix = this.functionPrefixes.Where(value => value.Length != 0 && candidate.StartsWith(value, StringComparison.Ordinal)).OrderByDescending(value => value.Length).FirstOrDefault();
            if (prefix != null)
            {
                candidate = candidate[prefix.Length..];
            }

            if (this.functionNamingConvention == NamingConvention.Unknown)
            {
                return candidate;
            }

            string[] parts = GetCsCleanName(candidate).SplitByCase();
            StringBuilder sb = new();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (this.ignoredParts.Contains(part))
                {
                    continue;
                }

                sb.Append(part);
            }

            return NamingHelper.ConvertTo(sb.ToString(), this.functionNamingConvention);
        }

        /// <summary>
        /// Resolves a configured per-function parameter default and projects typedef enum names when required.
        /// </summary>
        /// <param name="functionName">
        /// The exact native function identifier used to find its mapping.
        /// </param>
        /// <param name="parameter">
        /// The native parameter whose name and type determine default lookup and projection.
        /// </param>
        /// <param name="defaultValue">
        /// Receives the managed default expression, or null when no configured default exists.
        /// </param>
        /// <returns>
        /// True when the function mapping contains a default for this parameter.
        /// </returns>
        public bool TryGetDefaultValue(
            string functionName,
            CppParameter parameter,
            out string? defaultValue
        ) {
            if (TryGetFunctionMapping(functionName, out var mapping))
            {
                if (mapping.defaults.TryGetValue(parameter.name, out var value))
                {
                    if (parameter.type is CppTypedef typedef && typedef.elementType is CppPrimitiveType type)
                    {
                        if (value.IsNumeric())
                        {
                            defaultValue = value;
                            return true;
                        }

                        string csName = GetCsCleanName(typedef.name);
                        EnumPrefix enumNamePrefix = GetEnumNamePrefix(typedef.name);
                        if (csName.EndsWith("_"))
                        {
                            csName = csName.Remove(csName.Length - 1);
                        }

                        var enumItemName = GetEnumName(value, enumNamePrefix);
                        defaultValue = $"{csName}.{enumItemName}";
                        return true;
                    }

                    defaultValue = NormalizeValue(value);
                    return true;
                }
            }

            defaultValue = null;
            return false;
        }

        /// <summary>
        /// Projects a constant identifier using an explicit override or the configured constant naming convention.
        /// </summary>
        /// <param name="value">
        /// The native constant identifier to project.
        /// </param>
        /// <returns>
        /// The override name or the cleaned convention-based managed identifier.
        /// </returns>
        public string GetConstantName(string value)
        {
            if (this.knownConstantNames.TryGetValue(value, out string? knownName))
            {
                return knownName;
            }

            return GetCsCleanNameWithConvention(value, this.constantNamingConvention, false);
        }

        /// <summary>
        /// Derives enum-prefix word fragments while grouping numeric fragments with adjacent words.
        /// </summary>
        /// <param name="typeName">
        /// The native enum or item identifier whose prefix words are needed.
        /// </param>
        /// <returns>
        /// Configured underscore-delimited prefix fragments or inferred word fragments.
        /// </returns>
        public EnumPrefix GetEnumNamePrefix(string typeName)
        {
            if (this.knownEnumPrefixes.TryGetValue(typeName, out string? knownValue))
            {
                return new(knownValue.Split('_'));
            }

            string[] parts = typeName.Split('_', StringSplitOptions.RemoveEmptyEntries).SelectMany(x => x.SplitByCase()).ToArray();
            List<string> partList = new();
            bool mergeWithLast = false;
            int last = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if ((part.IsNumeric()) && !mergeWithLast)
                {
                    if (i == 0 && parts.Length > 1)
                    {
                        mergeWithLast = true;
                    }
                    else if (i > 0)
                    {
                        partList[last] += part;
                    }
                    else
                    {
                        last = partList.Count;
                        partList.Add(part);
                    }
                }
                else if (mergeWithLast)
                {
                    last = partList.Count;
                    partList.Add(parts[last] + part);
                    mergeWithLast = false;
                }
                else
                {
                    last = partList.Count;
                    partList.Add(part);
                }
            }

            return new(partList.ToArray());
        }

        /// <summary>
        /// Builds enum-prefix candidates including contiguous combinations for constant-group matching.
        /// </summary>
        /// <param name="typeName">
        /// The native prefix identifier to expand.
        /// </param>
        /// <returns>
        /// Configured prefix fragments or ordered inferred fragments and combinations.
        /// </returns>
        public EnumPrefix GetEnumNamePrefixEx(string typeName)
        {
            if (this.knownEnumPrefixes.TryGetValue(typeName, out string? knownValue))
            {
                return new(knownValue.Split('_'));
            }

            string[] parts = typeName.Split('_', StringSplitOptions.RemoveEmptyEntries).SelectMany(x => x.SplitByCase()).ToArray();
            List<string> partList = new();
            string compositeA = "";
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                partList.Add(part);
                compositeA += part;
                if (!partList.Contains(compositeA))
                {
                    partList.Add(compositeA);
                }

                string compositeB = "";
                for (int j = i; j < parts.Length; j++)
                {
                    compositeB += parts[j];
                    if (!partList.Contains(compositeB))
                    {
                        partList.Add(compositeB);
                    }
                }

                var subParts = part.SplitByCase();
                if (subParts.Length > 1)
                {
                    partList.AddRange(subParts);
                    string composite = "";
                    for (int j = 0; j < subParts.Length - 1; j++)
                    {
                        composite += subParts[j];
                        if (!partList.Contains(composite))
                        {
                            partList.Add(composite);
                        }
                    }
                }
            }

            return new([.. partList]);
        }

        /// <summary>
        /// Applies an explicit enum-item name override before the inferred prefix-removal naming policy.
        /// </summary>
        /// <param name="value">
        /// The native enum-item identifier or a hexadecimal literal spelling.
        /// </param>
        /// <param name="enumPrefix">
        /// The inferred or configured prefix fragments to remove before the first retained word.
        /// </param>
        /// <returns>
        /// The configured or normalized managed item name; hexadecimal literal spellings are retained.
        /// </returns>
        public string GetEnumNameEx(
            string value,
            EnumPrefix enumPrefix
        ) {
            if (this.knownEnumValueNames.TryGetValue(value, out string? knownName))
            {
                return knownName;
            }

            return GetEnumName(value, enumPrefix);
        }

        /// <summary>
        /// Projects an enum-item name after removing ignored words and matching leading prefix fragments.
        /// </summary>
        /// <param name="value">
        /// The native enum-item identifier or a hexadecimal literal spelling.
        /// </param>
        /// <param name="enumPrefix">
        /// The inferred or configured prefix fragments to remove before the first retained word.
        /// </param>
        /// <returns>
        /// The configured or normalized managed item name; hexadecimal literal spellings are retained.
        /// </returns>
        public string GetEnumName(
            string value,
            EnumPrefix enumPrefix
        ) {
            if (value.StartsWith("0x"))
                return value;
            IReadOnlyList<string> parts = GetEnumNamePrefix(value).parts;
            IReadOnlyList<string> prefixParts = enumPrefix.parts;
            bool capture = false;
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                string part = parts[i];
                if (this.ignoredParts.Contains(part, StringComparer.InvariantCultureIgnoreCase) || prefixParts.Contains(part, StringComparer.InvariantCultureIgnoreCase) && !capture)
                {
                    continue;
                }

                part = part.ToLowerInvariant();
                bool wasNum = false;
                for (int j = 0; j < part.Length; j++)
                {
                    var c = part[j];
                    if (j == 0 || wasNum)
                    {
                        sb.Append(char.ToUpperInvariant(c));
                        wasNum = false;
                    }
                    else if (char.IsDigit(c))
                    {
                        sb.Append(c);
                        wasNum = true;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                capture = true;
            }

            if (sb.Length == 0)
            {
                sb.Append(parts[^1].ToTitleCaseFragment());
            }

            string prettyName = sb.ToString();
            string finalName = char.IsNumber(prettyName[0]) ? parts[^1].ToTitleCaseFragment() + prettyName : prettyName;
            return NamingHelper.ConvertTo(finalName, this.enumItemNamingConvention);
        }

        /// <summary>
        /// Derives the uppercase underscore-delimited prefix used for native extension method matching.
        /// </summary>
        /// <param name="typeName">
        /// The native receiver type identifier.
        /// </param>
        /// <returns>
        /// The configured override or invariant uppercase word fragments joined with underscores.
        /// </returns>
        public string GetExtensionNamePrefix(string typeName)
        {
            if (this.knownExtensionPrefixes.TryGetValue(typeName, out string? knownValue))
            {
                return knownValue;
            }

            string[] parts = typeName.Split('_', StringSplitOptions.RemoveEmptyEntries).SelectMany(x => x.SplitByCase()).ToArray();
            return string.Join("_", parts.Select(s => s.ToUpperInvariant()));
        }

        /// <summary>
        /// Projects an extension method identifier after removing ignored words and matching prefix fragments.
        /// </summary>
        /// <param name="value">
        /// The native function identifier to project.
        /// </param>
        /// <param name="extensionPrefix">
        /// The underscore-delimited receiver prefix whose leading fragments should be removed.
        /// </param>
        /// <returns>
        /// The configured override or title-cased extension method name.
        /// </returns>
        public string GetExtensionName(
            string value,
            string extensionPrefix
        ) {
            if (this.knownExtensionNames.TryGetValue(value, out string? knownName))
            {
                return knownName;
            }

            if (this.extensionNamingConvention == NamingConvention.Unknown)
            {
                return value;
            }

            string[] parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries).SelectMany(x => x.SplitByCase()).ToArray();
            string[] prefixParts = extensionPrefix.Split('_', StringSplitOptions.RemoveEmptyEntries);
            bool capture = false;
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (prefixParts.Contains(part, StringComparer.InvariantCultureIgnoreCase) && !capture)
                {
                    continue;
                }

                part = part.ToLowerInvariant();
                sb.Append(char.ToUpper(part[0]));
                sb.Append(part[1..]);
                capture = true;
            }

            if (sb.Length == 0)
                sb.Append(value);
            string prettyName = sb.ToString();
            string finalName = (char.IsNumber(prettyName[0])) ? prefixParts[^1].ToTitleCaseFragment() + prettyName : prettyName;
            return NamingHelper.ConvertTo(finalName, this.extensionNamingConvention);
        }

        /// <summary>
        /// Applies the field naming convention and escapes keywords and leading digits.
        /// </summary>
        /// <param name="name">
        /// The native field identifier.
        /// </param>
        /// <returns>
        /// A managed field identifier, or an empty string when normalization removes all characters.
        /// </returns>
        public string GetFieldName(string name)
        {
            name = NamingHelper.ConvertTo(name, this.memberNamingConvention);
            if (IsCSharpKeyword(name))
            {
                return "@" + name;
            }

            if (name.Length == 0)
                return name;
            return char.IsDigit(name[0]) ? '_' + name : name;
        }

        /// <summary>
        /// Uses an explicit mapped field identifier when present, escaping keywords and leading digits.
        /// </summary>
        /// <param name="name">
        /// The native identifier used when no explicit mapping is supplied.
        /// </param>
        /// <param name="mappedName">
        /// The explicit managed spelling, or null or whitespace to apply the normal naming convention.
        /// </param>
        /// <returns>
        /// A managed field identifier honoring the explicit mapping when supplied.
        /// </returns>
        public string GetFieldName(
            string name,
            string? mappedName
        ) {
            if (string.IsNullOrWhiteSpace(mappedName))
            {
                return GetFieldName(name);
            }

            if (IsCSharpKeyword(mappedName))
            {
                return "@" + mappedName;
            }

            return char.IsDigit(mappedName[0]) ? '_' + mappedName : mappedName;
        }

        private bool IsCSharpKeyword(string value) => this.keywords.Contains(value) || SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None;
        /// <summary>
        /// Resolves the configured managed carrier for native Boolean values.
        /// </summary>
        /// <returns>
        /// The Bool8, Bool32, byte, or int carrier spelling.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// The configured Boolean carrier is not recognized.
        /// </exception>
        public string GetBoolType()
        {
            return this.boolType switch
            {
                BoolType.Bool8 => "Bool8",
                BoolType.Bool32 => "Bool32",
                BoolType.Byte => "byte",
                BoolType.Int32 => "int",
                _ => throw new NotSupportedException(),
            };
        }

        /// <summary>
        /// Escapes plain comment text and writes a summary block to the supplied output writer.
        /// </summary>
        /// <param name="comment">
        /// The plain native comment text, or null to apply the placeholder-comment policy.
        /// </param>
        /// <param name="writer">
        /// The output writer that receives the generated documentation.
        /// </param>
        /// <returns>
        /// True when a summary block is written; false when comments are absent and placeholders are disabled.
        /// </returns>
        public bool WriteCsSummary(
            string? comment,
            ICodeWriter writer
        ) {
            if (comment == null)
            {
                if (this.generatePlaceholderComments)
                {
                    writer.WriteLine("/// <summary>");
                    writer.WriteLine("/// To be documented.");
                    writer.WriteLine("/// </summary>");
                    return true;
                }

                return false;
            }

            var lines = comment.Replace("/", string.Empty).Split("\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            writer.WriteLine("/// <summary>");
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                writer.WriteLine($"/// {new XText(line)}<br/>");
            }

            writer.WriteLine($"/// </summary>");
            return true;
        }

        /// <summary>
        /// Escapes plain comment text and prepares its managed summary block.
        /// </summary>
        /// <param name="comment">
        /// The plain native comment text, or null to apply the placeholder-comment policy.
        /// </param>
        /// <param name="com">
        /// Receives the generated summary block, or null when no documentation is produced.
        /// </param>
        /// <returns>
        /// True when a summary block is prepared; false when no documentation is produced.
        /// </returns>
        public bool WriteCsSummary(
            string? comment,
            out string? com
        ) {
            com = null;
            StringBuilder sb = new();
            if (comment == null)
            {
                if (this.generatePlaceholderComments)
                {
                    sb.AppendLine("/// <summary>");
                    sb.AppendLine("/// To be documented.");
                    sb.AppendLine("/// </summary>");
                    com = sb.ToString();
                    return true;
                }

                return false;
            }

            var lines = comment.Replace("/", string.Empty).Split("\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            sb.AppendLine("/// <summary>");
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                sb.AppendLine($"/// {new XText(line)}<br/>");
            }

            sb.AppendLine($"/// </summary>");
            com = sb.ToString();
            return true;
        }

        /// <summary>
        /// Escapes plain native comment text into a managed summary block.
        /// </summary>
        /// <param name="comment">
        /// The native comment text, or null to apply the placeholder-comment policy.
        /// </param>
        /// <returns>
        /// The generated summary block, or null when no documentation is produced.
        /// </returns>
        public string? WriteCsSummary(string? comment)
        {
            WriteCsSummary(comment, out var result);
            return result;
        }

        /// <summary>
        /// Projects parsed native documentation into summary, parameter, type-parameter, and return tags.
        /// </summary>
        /// <param name="comment">
        /// The parsed native documentation tree, or null to apply the placeholder-comment policy.
        /// </param>
        /// <param name="writer">
        /// The writer that receives the generated documentation.
        /// </param>
        /// <returns>
        /// True when documentation is emitted; false when no documentation is available.
        /// </returns>
        public bool WriteCsSummary(
            CppComment? comment,
            ICodeWriter writer
        ) {
            string? documentation = BuildCsDocumentation(comment);
            if (documentation == null)
            {
                return false;
            }

            writer.WriteLines(documentation);
            return true;
        }

        /// <summary>
        /// Projects parsed native documentation into managed XML without writing output.
        /// </summary>
        /// <param name="cppComment">
        /// The borrowed native documentation tree, or null to apply the placeholder-comment policy.
        /// </param>
        /// <param name="comment">
        /// Receives the generated XML comment text, or null when no documentation is available.
        /// </param>
        public void WriteCsSummary(
            CppComment? cppComment,
            out string? comment
        ) {
            comment = BuildCsDocumentation(cppComment);
        }

        private string? BuildCsDocumentation(CppComment? comment)
        {
            if (comment == null || comment.kind == CppCommentKind.Null)
            {
                return this.generatePlaceholderComments ? "/// <summary>\n/// To be documented.\n/// </summary>\n" : null;
            }

            List<string> summaryParts = [];
            List<(string Name, string Text)> parameters = [];
            List<(string Name, string Text)> typeParameters = [];
            List<string> returns = [];
            CollectDocumentation(comment, summaryParts, parameters, typeParameters, returns);
            string summary = NormalizeDocumentationText(string.Join(" ", summaryParts));
            if (summary.Length == 0 && this.generatePlaceholderComments)
            {
                summary = "To be documented.";
            }

            if (summary.Length == 0 && parameters.Count == 0 && typeParameters.Count == 0 && returns.Count == 0)
            {
                return null;
            }

            StringBuilder builder = new();
            if (summary.Length > 0)
            {
                builder.AppendLine("/// <summary>");
                builder.Append("/// ").AppendLine(EscapeDocumentation(summary));
                builder.AppendLine("/// </summary>");
            }

            foreach ((string name, string text) in parameters)
            {
                builder.Append("/// <param name=\"").Append(EscapeDocumentation(name)).Append("\">").Append(EscapeDocumentation(NormalizeDocumentationText(text))).AppendLine("</param>");
            }

            foreach ((string name, string text) in typeParameters)
            {
                builder.Append("/// <typeparam name=\"").Append(EscapeDocumentation(name)).Append("\">").Append(EscapeDocumentation(NormalizeDocumentationText(text))).AppendLine("</typeparam>");
            }

            if (returns.Count > 0)
            {
                builder.Append("/// <returns>").Append(EscapeDocumentation(NormalizeDocumentationText(string.Join(" ", returns)))).AppendLine("</returns>");
            }

            return builder.ToString();
        }

        private static void CollectDocumentation(
            CppComment comment,
            List<string> summaryParts,
            List<(string Name, string Text)> parameters,
            List<(string Name, string Text)> typeParameters,
            List<string> returns
        ) {
            if (comment is CppCommentParamCommand parameter)
            {
                parameters.Add((parameter.paramName, CollectDocumentationText(parameter)));
                return;
            }

            if (comment is CppCommentTemplateParamCommand typeParameter)
            {
                typeParameters.Add((typeParameter.paramName, CollectDocumentationText(typeParameter)));
                return;
            }

            if (comment is CppCommentBlockCommand command && command.commandName is "return" or "returns" or "result")
            {
                returns.Add(CollectDocumentationText(command));
                return;
            }

            if (comment is CppCommentText text)
            {
                if (!string.IsNullOrWhiteSpace(text.text))
                {
                    summaryParts.Add(text.text);
                }

                return;
            }

            if (comment.children == null)
            {
                return;
            }

            foreach (CppComment child in comment.children)
            {
                CollectDocumentation(child, summaryParts, parameters, typeParameters, returns);
            }
        }

        private static string CollectDocumentationText(CppComment comment)
        {
            List<string> parts = [];
            CollectText(comment, parts);
            return string.Join(" ", parts);
        }

        private static void CollectText(
            CppComment comment,
            List<string> parts
        ) {
            if (comment is CppCommentText text)
            {
                if (!string.IsNullOrWhiteSpace(text.text))
                {
                    parts.Add(text.text);
                }

                return;
            }

            if (comment.children == null)
            {
                return;
            }

            foreach (CppComment child in comment.children)
            {
                CollectText(child, parts);
            }
        }

        private static string NormalizeDocumentationText(string value)
        {
            return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        private static string EscapeDocumentation(string value) => new XText(value).ToString();
        /// <summary>
        /// Projects parsed native documentation into managed XML comment text.
        /// </summary>
        /// <param name="cppComment">
        /// The borrowed native documentation tree, or null to apply the placeholder-comment policy.
        /// </param>
        /// <returns>
        /// The generated XML comment block, or null when no documentation is available.
        /// </returns>
        public string? WriteCsSummary(CppComment? cppComment)
        {
            WriteCsSummary(cppComment, out var result);
            return result;
        }
    }
}
