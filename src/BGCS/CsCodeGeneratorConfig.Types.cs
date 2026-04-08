namespace BGCS
{
    using BGCS.Conversion;
    using BGCS.Core;
    using BGCS.Core.Mapping;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Model.Types;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Text;
    using System.Xml.Linq;

    /// <summary>
    /// Defines the public class <c>CsCodeGeneratorConfig</c> used by the generation pipeline.
    /// </summary>
    public partial class CsCodeGeneratorConfig
    {
        #region Mapping Helpers

        /// <summary>
        /// Performs the operation implemented by <c>TryGetEnumMapping</c>.
        /// </summary>
        /// <returns>Result produced by <c>TryGetEnumMapping</c>.</returns>
        public bool TryGetEnumMapping(string enumName, [NotNullWhen(true)] out EnumMapping? mapping)
        {
            for (int i = 0; i < EnumMappings.Count; i++)
            {
                var enumMapping = EnumMappings[i];
                if (enumMapping.ExportedName == enumName)
                {
                    mapping = enumMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetEnumMapping</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetEnumMapping</c>.</returns>
        public EnumMapping? GetEnumMapping(string enumName)
        {
            for (int i = 0; i < EnumMappings.Count; i++)
            {
                var enumMapping = EnumMappings[i];
                if (enumMapping.ExportedName == enumName)
                {
                    return enumMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Performs the operation implemented by <c>TryGetFunctionMapping</c>.
        /// </summary>
        /// <returns>Result produced by <c>TryGetFunctionMapping</c>.</returns>
        public bool TryGetFunctionMapping(string functionName, [NotNullWhen(true)] out FunctionMapping? mapping)
        {
            for (int i = 0; i < FunctionMappings.Count; i++)
            {
                var functionMapping = FunctionMappings[i];
                if (functionMapping.ExportedName == functionName)
                {
                    mapping = functionMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetFunctionMapping</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetFunctionMapping</c>.</returns>
        public FunctionMapping? GetFunctionMapping(string functionName)
        {
            for (int i = 0; i < FunctionMappings.Count; i++)
            {
                var functionMapping = FunctionMappings[i];
                if (functionMapping.ExportedName == functionName)
                {
                    return functionMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Performs the operation implemented by <c>TryGetTypeMapping</c>.
        /// </summary>
        /// <returns>Result produced by <c>TryGetTypeMapping</c>.</returns>
        public bool TryGetTypeMapping(string typeName, [NotNullWhen(true)] out TypeMapping? mapping)
        {
            for (int i = 0; i < ClassMappings.Count; i++)
            {
                var structMapping = ClassMappings[i];
                if (structMapping.ExportedName == typeName)
                {
                    mapping = structMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetTypeMapping</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetTypeMapping</c>.</returns>
        public TypeMapping? GetTypeMapping(string typeName)
        {
            for (int i = 0; i < ClassMappings.Count; i++)
            {
                var structMapping = ClassMappings[i];
                if (structMapping.ExportedName == typeName)
                {
                    return structMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Performs the operation implemented by <c>TryGetHandleMapping</c>.
        /// </summary>
        /// <returns>Result produced by <c>TryGetHandleMapping</c>.</returns>
        public bool TryGetHandleMapping(string typeName, [NotNullWhen(true)] out HandleMapping? mapping)
        {
            for (int i = 0; i < HandleMappings.Count; i++)
            {
                var handleMapping = HandleMappings[i];
                if (handleMapping.ExportedName == typeName)
                {
                    mapping = handleMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetHandleMapping</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetHandleMapping</c>.</returns>
        public HandleMapping? GetHandleMapping(string typeName)
        {
            for (int i = 0; i < HandleMappings.Count; i++)
            {
                var handleMapping = HandleMappings[i];
                if (handleMapping.ExportedName == typeName)
                {
                    return handleMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Performs the operation implemented by <c>TryGetDelegateMapping</c>.
        /// </summary>
        /// <returns>Result produced by <c>TryGetDelegateMapping</c>.</returns>
        public bool TryGetDelegateMapping(string delegateName, [NotNullWhen(true)] out DelegateMapping? mapping)
        {
            for (int i = 0; i < DelegateMappings.Count; i++)
            {
                var delegateMapping = DelegateMappings[i];
                if (delegateMapping.Name == delegateName)
                {
                    mapping = delegateMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetDelegateMapping</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetDelegateMapping</c>.</returns>
        public DelegateMapping? GetDelegateMapping(string delegateName)
        {
            for (int i = 0; i < DelegateMappings.Count; i++)
            {
                var delegateMapping = DelegateMappings[i];
                if (delegateMapping.Name == delegateName)
                {
                    return delegateMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Performs the operation implemented by <c>TryGetArrayMapping</c>.
        /// </summary>
        /// <returns>Result produced by <c>TryGetArrayMapping</c>.</returns>
        public bool TryGetArrayMapping(CppArrayType arrayType, [NotNullWhen(true)] out string? mapping)
        {
            for (int i = 0; i < ArrayMappings.Count; i++)
            {
                var map = ArrayMappings[i];
                if (map.Primitive == arrayType.GetPrimitiveKind(false) && map.Size == arrayType.Size)
                {
                    mapping = map.Name;
                    return true;
                }
            }
            mapping = null;
            return false;
        }

        #endregion Mapping Helpers

        /// <summary>
        /// Performs the operation implemented by <c>GetCsReturnType</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetCsReturnType</c>.</returns>
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
        /// Performs the operation implemented by <c>GetCsTypeName</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetCsTypeName</c>.</returns>
        public string GetCsTypeName(CppType? type)
        {
            if (type == null)
                return string.Empty;

            var name = GetCsTypeNameInternal(type);

            return name;
        }

        /// <summary>
        /// Performs the operation implemented by <c>MakeDelegatePointer</c>.
        /// </summary>
        /// <returns>Result produced by <c>MakeDelegatePointer</c>.</returns>
        public string MakeDelegatePointer(CppFunctionType functionType, bool withConvention = false)
        {
            if (withConvention)
            {
                if (functionType.Parameters.Count == 0)
                {
                    return $"delegate* unmanaged[{functionType.CallingConvention.GetCallingConventionDelegate()}]<{GetCsTypeNameInternal(functionType.ReturnType)}>";
                }
                else
                {
                    return $"delegate* unmanaged[{functionType.CallingConvention.GetCallingConventionDelegate()}]<{GetNamelessParameterSignature(functionType.Parameters, false, true)}, {GetCsTypeNameInternal(functionType.ReturnType)}>";
                }
            }
            else
            {
                if (functionType.Parameters.Count == 0)
                {
                    return $"delegate*<{GetCsTypeNameInternal(functionType.ReturnType)}>";
                }
                else
                {
                    return $"delegate*<{GetNamelessParameterSignature(functionType.Parameters, false, true)}, {GetCsTypeNameInternal(functionType.ReturnType)}>";
                }
            }
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetDelegatePointerType</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetDelegatePointerType</c>.</returns>
        public string GetDelegatePointerType(CppFunctionType functionType, bool withConvention = false)
        {
            if (DelegatesAsVoidPointer)
            {
                return "void*";
            }

            return MakeDelegatePointer(functionType, withConvention);
        }

        private string GetCsTypeNameInternal(CppType type)
        {
            return converter.Convert(type, CsTypeStyle.Raw);
        }

        /// <summary>
        /// This method will return <see langword="ref"/> <see cref="T"/> instead of <see cref="T"/>*
        /// </summary>
        ///
        /// <returns></returns>
        public string GetCsWrapperTypeName(CppType? type)
        {
            if (type == null)
                return string.Empty;

            var name = converter.Convert(type, CsTypeStyle.Ref);

            return name;
        }

        /// <summary>
        /// This method will return <see cref="Pointer&lt;T&gt;"/> instead of <see cref="T"/>*
        /// </summary>
        ///
        /// <returns></returns>
        public string GetCsWrappedPointerTypeName(CppType? type)
        {
            if (type == null)
                return string.Empty;

            var name = converter.Convert(type, CsTypeStyle.Wrapped);

            return name;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetParameterSignature</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetParameterSignature</c>.</returns>
        public string GetParameterSignature(IList<CppParameter> parameters, bool canUseOut, bool attributes = true, bool names = true, bool delegateType = false, bool compatibility = false)
        {
            StringBuilder argumentBuilder = new();
            int index = 0;

            for (int i = 0; i < parameters.Count; i++)
            {
                CppParameter cppParameter = parameters[i];
                var paramCsTypeName = GetCsTypeName(cppParameter.Type);
                var paramCsName = GetParameterName(i, cppParameter.Name);

                CppType ptrType = cppParameter.Type;
                int depth = 0;
                if (cppParameter.Type.IsPointer(ref depth, out var pointerType))
                {
                    ptrType = pointerType;
                }

                if ((delegateType || ptrType != cppParameter.Type) && ptrType is CppTypedef typedef && typedef.ElementType.IsDelegate(out var cppFunction) && !paramCsTypeName.Contains('*'))
                {
                    paramCsTypeName = GetDelegatePointerType(cppFunction);

                    while (depth-- > 0)
                    {
                        paramCsTypeName += "*";
                    }
                }

                if (attributes && GenerateMetadata)
                {
                    argumentBuilder.Append($"[NativeName(NativeNameType.Param, \"{cppParameter.Name}\")] ");
                    argumentBuilder.Append($"[NativeName(NativeNameType.Type, \"{cppParameter.Type.GetDisplayName()}\")] ");
                }

                if (paramCsTypeName == "bool")
                {
                    paramCsTypeName = GetBoolType();
                }

                if (canUseOut && cppParameter.Type.CanBeUsedAsOutput(out CppTypeDeclaration? cppTypeDeclaration))
                {
                    argumentBuilder.Append("out ");
                    paramCsTypeName = GetCsTypeName(cppTypeDeclaration);
                }

                if (compatibility && paramCsTypeName.Contains('*'))
                {
                    paramCsTypeName = "nint";
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
        /// Performs the operation implemented by <c>GetParameterSignatureNames</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetParameterSignatureNames</c>.</returns>
        public string GetParameterSignatureNames(IList<CppParameter> parameters)
        {
            StringBuilder argumentBuilder = new();
            int index = 0;

            for (int i = 0; i < parameters.Count; i++)
            {
                CppParameter cppParameter = parameters[i];
                var paramCsTypeName = GetCsTypeName(cppParameter.Type);
                var paramCsName = GetParameterName(i, cppParameter.Name);

                CppType ptrType = cppParameter.Type;
                int depth = 0;
                if (cppParameter.Type.IsPointer(ref depth, out var pointerType))
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
        /// Performs the operation implemented by <c>GetNamelessParameterSignature</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetNamelessParameterSignature</c>.</returns>
        public string GetNamelessParameterSignature(IList<CppParameter> parameters, bool canUseOut, bool delegateType = false, bool compatibility = false)
        {
            var argumentBuilder = new StringBuilder();
            int index = 0;

            foreach (CppParameter cppParameter in parameters)
            {
                string direction = string.Empty;
                var paramCsTypeName = GetCsTypeName(cppParameter.Type);

                CppType ptrType = cppParameter.Type;
                int depth = 0;
                if (cppParameter.Type.IsPointer(ref depth, out var pointerType))
                {
                    ptrType = pointerType;
                }

                if (cppParameter.Type is CppQualifiedType qualifiedType)
                {
                    ptrType = qualifiedType.ElementType;
                }

                if (delegateType && ptrType is CppTypedef typedef && typedef.ElementType.IsDelegate(out var cppFunction))
                {
                    paramCsTypeName = GetDelegatePointerType(cppFunction);

                    while (depth-- > 0)
                    {
                        paramCsTypeName += "*";
                    }
                }

                if (paramCsTypeName == "bool")
                {
                    paramCsTypeName = "byte";
                }

                if (canUseOut && cppParameter.Type.CanBeUsedAsOutput(out CppTypeDeclaration? cppTypeDeclaration))
                {
                    argumentBuilder.Append("out ");
                    paramCsTypeName = GetCsTypeName(cppTypeDeclaration);
                }

                if (compatibility && paramCsTypeName.Contains('*'))
                {
                    paramCsTypeName = "nint";
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
        /// Performs the operation implemented by <c>WriteFunctionMarshalling</c>.
        /// </summary>
        /// <returns>Result produced by <c>WriteFunctionMarshalling</c>.</returns>
        public string WriteFunctionMarshalling(IList<CppParameter> parameters, bool compatibility = false)
        {
            var argumentBuilder = new StringBuilder();
            int index = 0;

            for (int i = 0; i < parameters.Count; i++)
            {
                CppParameter cppParameter = parameters[i];
                string direction = string.Empty;
                var paramCsTypeName = GetCsTypeName(cppParameter.Type);
                var paramCsName = GetParameterName(i, cppParameter.Name);

                CppType ptrType = cppParameter.Type;
                int depth = 0;
                if (cppParameter.Type.IsPointer(ref depth, out var pointerType))
                {
                    ptrType = pointerType;
                }

                if (cppParameter.Type is CppQualifiedType qualifiedType)
                {
                    ptrType = qualifiedType.ElementType;
                }

                if (ptrType is CppTypedef typedef && typedef.ElementType.IsDelegate())
                {
                    if (compatibility)
                    {
                        argumentBuilder.Append($"(nint){paramCsName}");
                    }
                    else
                    {
                        argumentBuilder.Append($"{paramCsName}");
                    }
                }
                else
                {
                    if (compatibility && (paramCsTypeName.Contains('*') || depth > 0))
                    {
                        argumentBuilder.Append($"(nint){paramCsName}");
                    }
                    else
                    {
                        argumentBuilder.Append($"{paramCsName}");
                    }
                }

                if (index < parameters.Count - 1)
                {
                    argumentBuilder.Append(", ");
                }

                index++;
            }

            return argumentBuilder.ToString();
        }

        private readonly ConcurrentDictionary<string, string> parameterNameCache = new();

        /// <summary>
        /// Performs the operation implemented by <c>GetParameterName</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetParameterName</c>.</returns>
        public string GetParameterName(int paramIdx, string name)
        {
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
            if (Keywords.Contains(name))
            {
                return "@" + name;
            }

            if (name.StartsWith('p') && name.Length > 1 && char.IsUpper(name[1]))
            {
            }

            if (name == string.Empty)
            {
                return $"unknown{paramIdx}";
            }

            return NormalizeParameterName(name);
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetDelegateName</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetDelegateName</c>.</returns>
        public string GetDelegateName(string name)
        {
            string newName = NamingHelper.ConvertTo(name, DelegateNamingConvention);
            foreach (var item in NameMappings)
            {
                newName = newName.Replace(item.Key, item.Value, StringComparison.InvariantCultureIgnoreCase);
            }

            return newName;
        }

        /// <summary>
        /// Performs the operation implemented by <c>NormalizeParameterName</c>.
        /// </summary>
        /// <returns>Result produced by <c>NormalizeParameterName</c>.</returns>
        public string NormalizeParameterName(string name)
        {
            if (parameterNameCache.TryGetValue(name, out var newName))
            {
                return newName;
            }

            name = name.Trim('_');

            newName = NamingHelper.ConvertTo(name, ParameterNamingConvention);

            if (Keywords.Contains(newName))
            {
                newName = "@" + newName;
            }

            if (char.IsDigit(newName[0]))
            {
                newName = "_" + newName;
            }

            parameterNameCache.TryAdd(name, newName);

            return newName;
        }

        /// <summary>
        /// Performs the operation implemented by <c>NormalizeValue</c>.
        /// </summary>
        /// <returns>Result produced by <c>NormalizeValue</c>.</returns>
        public string? NormalizeValue(string value, bool sanitize)
        {
            if (KnownDefaultValueNames.TryGetValue(value, out var names))
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
                return "float.MinValue";
            if (value == "-FLT_MIN")
                return "-float.MinValue";
            if (value == "nullptr")
                return "default";
            if (value == "false")
                return "0";
            if (value == "true")
                return "1";

            // TODO: needs refactoring. remove the ImVec!
            if (value.StartsWith("ImVec") && sanitize)
                return null;
            if (value.StartsWith("ImVec2"))
            {
                value = value[7..][..(value.Length - 8)];
                var parts = value.Split(',');
                return $"new Vector2({NormalizeValue(parts[0], sanitize)},{NormalizeValue(parts[1], sanitize)})";
            }
            if (value.StartsWith("ImVec4"))
            {
                value = value[7..][..(value.Length - 8)];
                var parts = value.Split(',');
                return $"new Vector4({NormalizeValue(parts[0], sanitize)},{NormalizeValue(parts[1], sanitize)},{NormalizeValue(parts[2], sanitize)},{NormalizeValue(parts[3], sanitize)})";
            }
            return value;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetCsFunctionName</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetCsFunctionName</c>.</returns>
        public string GetCsFunctionName(string function)
        {
            if (TryGetFunctionMapping(function, out var mapping))
            {
                if (mapping.FriendlyName != null)
                    return mapping.FriendlyName;
            }

            if (FunctionNamingConvention == NamingConvention.Unknown)
            {
                return function;
            }

            string[] parts = GetCsCleanName(function).SplitByCase();

            StringBuilder sb = new();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];

                if (IgnoredParts.Contains(part))
                {
                    continue;
                }

                sb.Append(part);
            }

            return NamingHelper.ConvertTo(sb.ToString(), FunctionNamingConvention);
        }

        /// <summary>
        /// Performs the operation implemented by <c>TryGetDefaultValue</c>.
        /// </summary>
        /// <returns>Result produced by <c>TryGetDefaultValue</c>.</returns>
        public bool TryGetDefaultValue(string functionName, CppParameter parameter, bool sanitize, out string? defaultValue)
        {
            if (TryGetFunctionMapping(functionName, out var mapping))
            {
                if (mapping.Defaults.TryGetValue(parameter.Name, out var value))
                {
                    if (parameter.Type is CppTypedef typedef && typedef.ElementType is CppPrimitiveType type)
                    {
                        if (value.IsNumeric())
                        {
                            defaultValue = value;
                            return true;
                        }

                        string csName = GetCsCleanName(typedef.Name);
                        EnumPrefix enumNamePrefix = GetEnumNamePrefix(typedef.Name);
                        if (csName.EndsWith("_"))
                        {
                            csName = csName.Remove(csName.Length - 1);
                        }
                        var enumItemName = GetEnumName(value, enumNamePrefix);

                        defaultValue = $"{csName}.{enumItemName}";
                        return true;
                    }
                    defaultValue = NormalizeValue(value, sanitize);
                    return true;
                }
            }
            defaultValue = null;
            return false;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetConstantName</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetConstantName</c>.</returns>
        public string GetConstantName(string value)
        {
            if (KnownConstantNames.TryGetValue(value, out string? knownName))
            {
                return knownName;
            }

            return GetCsCleanNameWithConvention(value, ConstantNamingConvention, false);
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetEnumNamePrefix</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetEnumNamePrefix</c>.</returns>
        public EnumPrefix GetEnumNamePrefix(string typeName)
        {
            if (KnownEnumPrefixes.TryGetValue(typeName, out string? knownValue))
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
        /// Performs the operation implemented by <c>GetEnumNamePrefixEx</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetEnumNamePrefixEx</c>.</returns>
        public EnumPrefix GetEnumNamePrefixEx(string typeName)
        {
            if (KnownEnumPrefixes.TryGetValue(typeName, out string? knownValue))
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
        /// Performs the operation implemented by <c>GetEnumNameEx</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetEnumNameEx</c>.</returns>
        public string GetEnumNameEx(string value, EnumPrefix enumPrefix)
        {
            return GetEnumName(value, enumPrefix);
            /*
            if (KnownEnumValueNames.TryGetValue(value, out string? knownName))
            {
                return knownName;
            }

            string[] parts = GetEnumNamePrefix(value).Parts;
            string[] prefixParts = enumPrefix.Parts;

            var name = GetCsCleanNameWithConvention(value, EnumNamingConvention, false);

            int lastRemoved = -1;
            for (int i = 0; i < prefixParts.Length; i++)
            {
                var part = prefixParts[i];
                if (name.StartsWith(part, StringComparison.InvariantCultureIgnoreCase) && name.Length - part.Length != 0)
                {
                    name = name[part.Length..];
                    lastRemoved = i;
                }
            }

            bool capture = false;
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (IgnoredParts.Contains(part, StringComparer.InvariantCultureIgnoreCase) || prefixParts.Contains(part, StringComparer.InvariantCultureIgnoreCase) && !capture)
                {
                    continue;
                }

                part = part.ToLower();

                bool wasNum = false;
                for (int j = 0; j < part.Length; j++)
                {
                    var c = part[j];
                    if (j == 0 || wasNum)
                    {
                        sb.Append(char.ToUpper(c));
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
                sb.Append(parts[^1].ToCamelCase());
            }

            string prettyName = sb.ToString();

            if (char.IsDigit(name[0]))
            {
                name = prefixParts[lastRemoved].ToCamelCase() + name;
            }

            return name; //char.IsNumber(prettyName[0]) ? parts[^1].ToCamelCase() + prettyName : prettyName;
            */
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetEnumName</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetEnumName</c>.</returns>
        public string GetEnumName(string value, EnumPrefix enumPrefix)
        {
            if (value.StartsWith("0x"))
                return value;

            string[] parts = GetEnumNamePrefix(value).Parts;
            string[] prefixParts = enumPrefix.Parts;

            bool capture = false;
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (IgnoredParts.Contains(part, StringComparer.InvariantCultureIgnoreCase) || prefixParts.Contains(part, StringComparer.InvariantCultureIgnoreCase) && !capture)
                {
                    continue;
                }

                part = part.ToLower();

                bool wasNum = false;
                for (int j = 0; j < part.Length; j++)
                {
                    var c = part[j];
                    if (j == 0 || wasNum)
                    {
                        sb.Append(char.ToUpper(c));
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
                sb.Append(parts[^1].ToCamelCase());
            }

            string prettyName = sb.ToString();
            string finalName = char.IsNumber(prettyName[0]) ? parts[^1].ToCamelCase() + prettyName : prettyName;
            return NamingHelper.ConvertTo(finalName, EnumItemNamingConvention);
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetExtensionNamePrefix</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetExtensionNamePrefix</c>.</returns>
        public string GetExtensionNamePrefix(string typeName)
        {
            if (KnownExtensionPrefixes.TryGetValue(typeName, out string? knownValue))
            {
                return knownValue;
            }

            string[] parts = typeName.Split('_', StringSplitOptions.RemoveEmptyEntries).SelectMany(x => x.SplitByCase()).ToArray();

            return string.Join("_", parts.Select(s => s.ToUpper()));
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetExtensionName</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetExtensionName</c>.</returns>
        public string GetExtensionName(string value, string extensionPrefix)
        {
            if (KnownExtensionNames.TryGetValue(value, out string? knownName))
            {
                return knownName;
            }

            if (ExtensionNamingConvention == NamingConvention.Unknown)
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

                part = part.ToLower();

                sb.Append(char.ToUpper(part[0]));
                sb.Append(part[1..]);
                capture = true;
            }

            if (sb.Length == 0)
                sb.Append(value);

            string prettyName = sb.ToString();
            string finalName = (char.IsNumber(prettyName[0])) ? prefixParts[^1].ToCamelCase() + prettyName : prettyName;
            return NamingHelper.ConvertTo(finalName, ExtensionNamingConvention);
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetFieldName</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetFieldName</c>.</returns>
        public string GetFieldName(string name)
        {
            var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
            StringBuilder sb = new();
            for (int i = 0; i < parts.Length; i++)
            {
                sb.Append(char.ToUpper(parts[i][0]));
                sb.Append(parts[i][1..]);
            }
            name = sb.ToString();
            if (Keywords.Contains(name))
            {
                return "@" + name;
            }

            if (name.Length == 0)
                return name;

            return char.IsDigit(name[0]) ? '_' + name : name;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetBoolType</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetBoolType</c>.</returns>
        public unsafe string GetBoolType()
        {
            return BoolType switch
            {
                BoolType.Bool8 => "Bool8",
                BoolType.Bool32 => "Bool32",
                _ => throw new NotSupportedException(),
            };
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetBoolType</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetBoolType</c>.</returns>
        public unsafe string GetBoolType(bool ptr)
        {
            return BoolType switch
            {
                BoolType.Bool8 => "Bool8",
                BoolType.Bool32 => "Bool32",
                _ => throw new NotSupportedException(),
            };
        }

        /// <summary>
        /// Performs the operation implemented by <c>WriteCsSummary</c>.
        /// </summary>
        /// <returns>Result produced by <c>WriteCsSummary</c>.</returns>
        public bool WriteCsSummary(string? comment, ICodeWriter writer)
        {
            if (comment == null)
            {
                if (GeneratePlaceholderComments)
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
        /// Performs the operation implemented by <c>WriteCsSummary</c>.
        /// </summary>
        /// <returns>Result produced by <c>WriteCsSummary</c>.</returns>
        public bool WriteCsSummary(string? comment, out string? com)
        {
            com = null;
            StringBuilder sb = new();
            if (comment == null)
            {
                if (GeneratePlaceholderComments)
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
        /// Performs the operation implemented by <c>WriteCsSummary</c>.
        /// </summary>
        /// <returns>Result produced by <c>WriteCsSummary</c>.</returns>
        public string? WriteCsSummary(string? comment)
        {
            WriteCsSummary(comment, out var result);
            return result;
        }

        /// <summary>
        /// Performs the operation implemented by <c>WriteCsSummary</c>.
        /// </summary>
        /// <returns>Result produced by <c>WriteCsSummary</c>.</returns>
        public bool WriteCsSummary(CppComment? comment, ICodeWriter writer)
        {
            bool result = false;
            if (comment is CppCommentFull full && full.Children != null)
            {
                writer.WriteLine("/// <summary>");
                for (int i = 0; i < full.Children.Count; i++)
                {
                    WriteCsSummary(full.Children[i], writer);
                }
                writer.WriteLine("/// </summary>");
                result = true;
            }
            if (comment is CppCommentParagraph paragraph)
            {
                for (int i = 0; i < paragraph.Children.Count; i++)
                {
                    WriteCsSummary(paragraph.Children[i], writer);
                }
                result = true;
            }

            if (comment is CppCommentBlockCommand blockCommand)
            {
            }
            if (comment is CppCommentVerbatimBlockCommand verbatimBlockCommand)
            {
            }

            if (comment is CppCommentVerbatimBlockLine verbatimBlockLine)
            {
            }

            if (comment is CppCommentVerbatimLine line)
            {
            }

            if (comment is CppCommentParamCommand paramCommand)
            {
                // TODO: add param comment support
            }

            if (comment is CppCommentInlineCommand inlineCommand)
            {
                // TODO: add inline comment support
            }

            if (comment is CppCommentText text)
            {
                writer.WriteLine($"/// " + text.Text + "<br/>");
                result = true;
            }

            if (comment == null || comment.Kind == CppCommentKind.Null)
            {
            }

            if (!result && GeneratePlaceholderComments)
            {
                writer.WriteLine("/// <summary>");
                writer.WriteLine("/// To be documented.");
                writer.WriteLine("/// </summary>");
                return true;
            }

            return result;
        }

        /// <summary>
        /// Performs the operation implemented by <c>WriteCsSummary</c>.
        /// </summary>
        public void WriteCsSummary(CppComment? cppComment, out string? comment)
        {
            comment = null;
            StringBuilder sb = new();
            if (cppComment is CppCommentFull full && full.Children != null)
            {
                sb.AppendLine("/// <summary>");
                for (int i = 0; i < full.Children.Count; i++)
                {
                    WriteCsSummary(full.Children[i], out var subComment);
                    sb.Append(subComment);
                }
                sb.AppendLine("/// </summary>");
                comment = sb.ToString();
                return;
            }
            if (cppComment is CppCommentParagraph paragraph)
            {
                for (int i = 0; i < paragraph.Children.Count; i++)
                {
                    WriteCsSummary(paragraph.Children[i], out var subComment);
                    sb.Append(subComment);
                }
                comment = sb.ToString();
                return;
            }
            if (cppComment is CppCommentText text)
            {
                sb.AppendLine($"/// " + text.Text + "<br/>");
                comment = sb.ToString();
                return;
            }

            if (cppComment is CppCommentBlockCommand blockCommand)
            {
                comment = null;
            }
            if (cppComment is CppCommentVerbatimBlockCommand verbatimBlockCommand)
            {
                comment = null;
            }

            if (cppComment is CppCommentVerbatimBlockLine verbatimBlockLine)
            {
                comment = null;
            }

            if (cppComment is CppCommentVerbatimLine line)
            {
                comment = null;
            }

            if (cppComment is CppCommentParamCommand paramCommand)
            {
                // TODO: add param comment support
                comment = null;
            }

            if (cppComment is CppCommentInlineCommand inlineCommand)
            {
                // TODO: add inline comment support
                comment = null;
            }

            if (cppComment == null || cppComment.Kind == CppCommentKind.Null)
            {
                comment = null;
            }

            if (comment == null && GeneratePlaceholderComments)
            {
                sb.AppendLine("/// <summary>");
                sb.AppendLine("/// To be documented.");
                sb.AppendLine("/// </summary>");
                comment = sb.ToString();
                return;
            }
        }

        /// <summary>
        /// Performs the operation implemented by <c>WriteCsSummary</c>.
        /// </summary>
        /// <returns>Result produced by <c>WriteCsSummary</c>.</returns>
        public string? WriteCsSummary(CppComment? cppComment)
        {
            WriteCsSummary(cppComment, out var result);
            return result;
        }
    }
}
