using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BGCS.Core.IO;
using BGCS.Core.Writing;
using BGCS.Cpp2C.Configuration;
using BGCS.Cpp2C.Lowering;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate.Bridges;

namespace BGCS.Cpp2C.Analysis;

/// <summary>
/// Lowers C++ classes, object lifetimes and callables into frozen bridge source operations.
/// </summary>
internal sealed class CppBridgeClassAnalyzer
{
    private readonly Cpp2CGeneratorConfig m_config;
    private readonly HashSet<string> m_definedFunctions = [];
    private readonly HashSet<string> m_definedTypes = [];
    private readonly Dictionary<(CppClass, CppFunction), string> m_mapping = [];
    private readonly Dictionary<string, string> m_cppQualifiedTypeNames = new(StringComparer.Ordinal);
    private static readonly string templateHeader = @"
#ifndef {PREFIX}COMMON_H
#define {PREFIX}COMMON_H

#include <stdint.h>
#include <stddef.h>
/* Calling convention */

#if defined(_WIN32) || defined(_WIN64)
#define {PREFIX}CALL __cdecl
#else
#define {PREFIX}CALL
#endif

/* API export/import */
#if defined(_WIN32) || defined(_WIN64)
#define {PREFIX}EXPORT_INTERNAL __declspec(dllexport)
#ifdef {PREFIX}BUILD_SHARED
#define {PREFIX}EXPORT __declspec(dllexport)
#else
#define {PREFIX}EXPORT __declspec(dllimport)
#endif
#elif defined(__GNUC__) || defined(__clang__)
#define {PREFIX}EXPORT_INTERNAL __attribute__((visibility(""default"")))
#ifdef {PREFIX}BUILD_SHARED
#define {PREFIX}EXPORT __attribute__((visibility(""default"")))
#else
#define {PREFIX}EXPORT
#endif
#else
#define {PREFIX}EXPORT_INTERNAL
#define {PREFIX}EXPORT
#endif

#if defined __cplusplus
#define {PREFIX}EXTERN extern ""C""
#else
#include <stdarg.h>
#include <stdbool.h>
#define {PREFIX}EXTERN extern
#endif

#define {PREFIX}API(type) {PREFIX}EXTERN {PREFIX}EXPORT type {PREFIX}CALL
#define {PREFIX}API_INTERNAL(type) {PREFIX}EXTERN {PREFIX}EXPORT_INTERNAL type {PREFIX}CALL

{PREFIX}API(const char*) {ERROR_PREFIX}GetLastError(void);
{PREFIX}API(void) {ERROR_PREFIX}ClearLastError(void);

#endif
";
    internal CppBridgeClassAnalyzer(Cpp2CGeneratorConfig config)
    {
        this.m_config = config;
    }

    internal IReadOnlyList<CppBridgeArtifact> Analyze(
        FileSet files,
        ParseResult result
    ) {
        var compilation = result.compilation;
        CppBridgeArtifact common = AnalyzeCommon(this.m_config);
        string headerName = $"Classes.h";
        using CppBridgeOperationBuilder headerWriter = new(CppIncludeBuilder.Create().AddInclude("common.h").AddInclude("enums.h").Build());
        CppIncludeBuilder cppIncludes = CppIncludeBuilder.Create();
        cppIncludes.AddInclude(headerName).AddSystemInclude("algorithm").AddSystemInclude("exception").AddSystemInclude("iterator").AddSystemInclude("stdexcept").AddSystemInclude("string").AddSystemInclude("utility");
        foreach (string sourceFile in result.entryFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            cppIncludes.AddInclude(Path.GetFileName(sourceFile));
        }

        string cppPreamble = $"#ifndef {this.m_config.namePrefix}BUILD_SHARED\n#define {this.m_config.namePrefix}BUILD_SHARED 1\n#endif\n" + cppIncludes.Build();
        using CppBridgeOperationBuilder cppWriter = new(cppPreamble);
        List<CppClass> allClasses = [.. compilation.classes];
        foreach (var ns in compilation.EnumerateNamespaces())
            allClasses.AddRange(ns.classes);
        this.m_cppQualifiedTypeNames.Clear();
        foreach (CppClass cppClass in allClasses)
            this.m_cppQualifiedTypeNames[cppClass.name] = cppClass.fullName;
        List<CppClass> classes = CppBridgeDeclarationPolicy.EnumerateAccessibleClasses(allClasses)
            .Where(cppClass => files.Contains(cppClass.sourceFile)).ToList();
        List<CppFunction> functions = compilation.functions.Where(function => files.Contains(function.sourceFile)).ToList();
        foreach (var ns in compilation.EnumerateNamespaces())
            functions.AddRange(ns.functions.Where(function => files.Contains(function.sourceFile)));
        List<CppFunction> allFunctions = [
            .. functions.Where(function => CppBridgeDeclarationPolicy.IsAccessible(function)
                && !m_config.IsCallableExcluded(null, function, m_config.GetCFunctionName(function))),
            .. classes.SelectMany(cppClass => GetBridgeFunctions(cppClass).Where(function =>
                CppBridgeDeclarationPolicy.IsAccessible(function) && !function.flags.HasFlag(CppFunctionFlags.Deleted)
                && !m_config.IsCallableExcluded(cppClass, function, $"{m_config.GetCTypeName(cppClass)}_{function.name}")))
        ];
        foreach (CppFunction function in allFunctions)
            RegisterSourceTypeSpellings(function);
        foreach (string requiredHeader in GetRequiredLoweringHeaders(allFunctions))
            cppWriter.WriteLine(requiredHeader.StartsWith('<') || requiredHeader.StartsWith('"') ? $"#include {requiredHeader}" : $"#include <{requiredHeader}>");
        WriteErrorSupport(cppWriter);
        WriteReferencedOpaqueTypes(classes, allFunctions, headerWriter);
        WriteSharedPtrSupport(allFunctions, headerWriter, cppWriter);
        WriteOpaqueValueSupport(allFunctions, headerWriter, cppWriter);
        WriteClasses(classes, headerWriter, cppWriter);
        WriteFreeFunctions(functions, headerWriter, cppWriter);
        return [common, headerWriter.Freeze("include/Classes.h"), cppWriter.Freeze("src/Classes.cpp")];
    }

    private void RegisterSourceTypeSpellings(CppFunction function)
    {
        this.m_config.RegisterSourceTypeSpelling(function.returnType, function.returnTypeSpelling);
        foreach (CppParameter parameter in function.parameters)
            this.m_config.RegisterSourceTypeSpelling(parameter.type, parameter.sourceTypeSpelling);
    }

    private static CppBridgeArtifact AnalyzeCommon(Cpp2CGeneratorConfig config)
    {
        using CppBridgeOperationBuilder writer = new();
        writer.Write(templateHeader.Replace("{PREFIX}", config.namePrefix).Replace("{ERROR_PREFIX}", config.errorSymbolPrefix));
        return writer.Freeze("include/common.h");
    }

    private void WriteErrorSupport(ICodeWriter writer)
    {
        writer.WriteLine($"static thread_local std::string {this.m_config.errorSymbolPrefix}last_error;");
        writer.WriteLine();
        using (writer.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(const char*) {this.m_config.errorSymbolPrefix}GetLastError(void)"))
        {
            writer.WriteLine($"return {this.m_config.errorSymbolPrefix}last_error.c_str();");
        }

        using (writer.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(void) {this.m_config.errorSymbolPrefix}ClearLastError(void)"))
        {
            writer.WriteLine($"{this.m_config.errorSymbolPrefix}last_error.clear();");
        }
    }

    private void WriteReferencedOpaqueTypes(
        IEnumerable<CppClass> classes,
        IEnumerable<CppFunction> functions,
        ICodeWriter headerWriter
    ) {
        IEnumerable<CppType> fieldTypes = classes.Where(cppClass => !RequiresOpaqueBridge(cppClass))
            .SelectMany(cppClass => cppClass.fields.Where(CppBridgeDeclarationPolicy.IsAccessible)
                .Select(field => field.type)).ToArray();
        IEnumerable<CppType> functionTypes = functions.SelectMany(function => new[] { function.returnType }.Concat(function.parameters.Select(parameter => parameter.type)));
        HashSet<string> generatedTypes = classes.Where(IsSupportedClass).Select(this.m_config.GetCTypeName).ToHashSet(StringComparer.Ordinal);
        HashSet<string> declarations = [];
        foreach (CppType type in fieldTypes.Concat(functionTypes))
        {
            if (!TryGetPointedClass(type, out CppClass? cppClass))
                continue;
            string name = this.m_config.GetCTypeName(cppClass!);
            if (!generatedTypes.Contains(name) && declarations.Add(name))
                headerWriter.WriteLine($"typedef struct {name} {name};");
        }

        foreach (CppType type in fieldTypes.Concat(functionTypes))
        {
            string cType;
            try
            {
                cType = this.m_config.GetCType(type);
            }
            catch
            {
                continue;
            }

            if (!cType.EndsWith('*'))
                continue;
            string name = cType.TrimEnd('*').Trim().Replace("const ", string.Empty, StringComparison.Ordinal);
            if (name is "void" or "char" or "wchar_t" or "bool" or "short" or "int" or "long" or "float" or "double" || name.StartsWith("uint", StringComparison.Ordinal) || name.StartsWith("int", StringComparison.Ordinal) || !name.StartsWith(this.m_config.namePrefix, StringComparison.Ordinal) || generatedTypes.Contains(name))
                continue;
            if (declarations.Add(name))
                headerWriter.WriteLine($"typedef struct {name} {name};");
        }

        if (declarations.Count > 0)
            headerWriter.WriteLine();
    }

    private static bool TryGetPointedClass(
        CppType type,
        out CppClass? cppClass
    ) {
        CppType current = type;
        bool indirect = false;
        while (true)
        {
            switch (current)
            {
                case CppPointerType pointer:
                    indirect = true;
                    current = pointer.elementType;
                    continue;
                case CppReferenceType reference:
                    indirect = true;
                    current = reference.elementType;
                    continue;
                case CppQualifiedType qualified:
                    current = qualified.elementType;
                    continue;
                case CppTypedef typedef:
                    current = typedef.elementType;
                    continue;
                default:
                    cppClass = indirect ? current as CppClass : null;
                    return cppClass != null;
            }
        }
    }

    private void WriteSharedPtrSupport(
        IEnumerable<CppFunction> functions,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        Dictionary<string, CppType> sharedTypes = [];
        foreach (CppFunction function in functions)
        {
            IEnumerable<CppType> types = new[]
            {
                function.returnType
            }.Concat(function.parameters.Select(parameter => parameter.type));
            foreach (CppType type in types.Where(this.m_config.IsSharedPtrType))
                sharedTypes.TryAdd(this.m_config.GetSharedPtrHolderName(type), type);
        }

        foreach ((string holder, CppType sharedType) in sharedTypes)
        {
            this.m_config.TryGetTemplateElementType(sharedType, out CppType? elementType);
            string elementCType = this.m_config.GetCType(elementType!);
            string sharedCppType = GetCppValueTypeName(sharedType);
            if (elementType is CppClass elementClass)
            {
                string elementHandle = this.m_config.GetCTypeName(elementClass);
                headerWriter.WriteLine($"typedef struct {elementHandle} {elementHandle};");
            }

            headerWriter.WriteLine($"typedef struct {holder} {holder};");
            headerWriter.WriteLine($"{this.m_config.namePrefix}API({elementCType}*) {holder}Get({holder}* self);");
            headerWriter.WriteLine($"{this.m_config.namePrefix}API({holder}*) {holder}Clone({holder}* self);");
            headerWriter.WriteLine($"{this.m_config.namePrefix}API(void) {holder}Destroy({holder}* self);");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({elementCType}*) {holder}Get({holder}* self)"))
                cppWriter.WriteLine($"return self == nullptr ? nullptr : reinterpret_cast<{sharedCppType}*>(self)->get();");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({holder}*) {holder}Clone({holder}* self)"))
                cppWriter.WriteLine($"return self == nullptr ? nullptr : reinterpret_cast<{holder}*>(new {sharedCppType}(*reinterpret_cast<{sharedCppType}*>(self))); ");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(void) {holder}Destroy({holder}* self)"))
                cppWriter.WriteLine($"delete reinterpret_cast<{sharedCppType}*>(self);");
        }
    }

    private void WriteOpaqueValueSupport(
        IEnumerable<CppFunction> functions,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        Dictionary<string, CppType> holderTypes = new(StringComparer.Ordinal);
        foreach (CppFunction function in functions)
        {
            foreach (CppType type in new[]
            {
                function.returnType
            }.Concat(function.parameters.Select(parameter => parameter.type)))
            {
                if (IsOpaqueValueType(type))
                    holderTypes.TryAdd(this.m_config.GetOpaqueValueHolderName(type), type);
            }
        }

        foreach ((string holder, CppType type) in holderTypes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            this.m_config.ResolveTypeLowering(type, CppTypeLoweringUse.Field);
            string cppType = GetCppValueTypeName(type);
            if (this.m_definedTypes.Add(holder))
                headerWriter.WriteLine($"typedef struct {holder} {holder};");
            headerWriter.WriteLine($"{this.m_config.namePrefix}API({holder}*) {holder}Create(void);");
            headerWriter.WriteLine($"{this.m_config.namePrefix}API({holder}*) {holder}Clone(const {holder}* self);");
            headerWriter.WriteLine($"{this.m_config.namePrefix}API(void) {holder}Destroy({holder}* self);");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({holder}*) {holder}Create(void)"))
                cppWriter.WriteLine($"return reinterpret_cast<{holder}*>(new {cppType}());");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({holder}*) {holder}Clone(const {holder}* self)"))
                cppWriter.WriteLine($"return self == nullptr ? nullptr : reinterpret_cast<{holder}*>(new {cppType}(*reinterpret_cast<const {cppType}*>(self)));");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(void) {holder}Destroy({holder}* self)"))
                cppWriter.WriteLine($"delete reinterpret_cast<{cppType}*>(self);");
            if (this.m_config.IsMapType(type))
                WriteMapSupport(type, holder, cppType, headerWriter, cppWriter);
            else if (this.m_config.IsSetType(type))
                WriteSetSupport(type, holder, cppType, headerWriter, cppWriter);
            else if (this.m_config.IsVariantType(type))
                WriteVariantSupport(type, holder, cppType, headerWriter, cppWriter);
            else if (this.m_config.IsExpectedType(type))
                WriteExpectedSupport(type, holder, cppType, headerWriter, cppWriter);
            headerWriter.WriteLine();
        }
    }

    private void WriteMapSupport(
        CppType type,
        string holder,
        string cppType,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        IReadOnlyList<CppType> arguments = this.m_config.GetTemplateTypeArguments(type);
        CppType key = arguments[0];
        CppType value = arguments[1];
        string cKey = this.m_config.GetCType(key);
        string cValue = this.m_config.GetCType(value);
        headerWriter.WriteLine($"{this.m_config.namePrefix}API(size_t) {holder}Size(const {holder}* self);");
        headerWriter.WriteLine($"{this.m_config.namePrefix}API(bool) {holder}Insert({holder}* self, {cKey} key, {cValue} value);");
        headerWriter.WriteLine($"{this.m_config.namePrefix}API(bool) {holder}TryGetAt(const {holder}* self, size_t index, {cKey}* out_key, {cValue}* out_value);");
        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(size_t) {holder}Size(const {holder}* self)"))
            cppWriter.WriteLine($"return self == nullptr ? 0 : reinterpret_cast<const {cppType}*>(self)->size();");
        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(bool) {holder}Insert({holder}* self, {cKey} key, {cValue} value)"))
        {
            cppWriter.WriteLine("if (self == nullptr) return false;");
            cppWriter.WriteLine($"return reinterpret_cast<{cppType}*>(self)->insert_or_assign({ConvertCToCpp(key, "key")}, {ConvertCToCpp(value, "value")}).second;");
        }

        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(bool) {holder}TryGetAt(const {holder}* self, size_t index, {cKey}* out_key, {cValue}* out_value)"))
        {
            cppWriter.WriteLine($"if (self == nullptr || index >= reinterpret_cast<const {cppType}*>(self)->size()) return false;");
            cppWriter.WriteLine($"auto iterator = reinterpret_cast<const {cppType}*>(self)->begin();");
            cppWriter.WriteLine("std::advance(iterator, index);");
            cppWriter.WriteLine($"if (out_key != nullptr) *out_key = {ConvertCppToC(key, "iterator->first")};");
            cppWriter.WriteLine($"if (out_value != nullptr) *out_value = {ConvertCppToC(value, "iterator->second")};");
            cppWriter.WriteLine("return true;");
        }
    }

    private void WriteSetSupport(
        CppType type,
        string holder,
        string cppType,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        CppType value = this.m_config.GetTemplateTypeArguments(type)[0];
        string cValue = this.m_config.GetCType(value);
        headerWriter.WriteLine($"{this.m_config.namePrefix}API(size_t) {holder}Size(const {holder}* self);");
        headerWriter.WriteLine($"{this.m_config.namePrefix}API(bool) {holder}Add({holder}* self, {cValue} value);");
        headerWriter.WriteLine($"{this.m_config.namePrefix}API(bool) {holder}TryGetAt(const {holder}* self, size_t index, {cValue}* out_value);");
        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(size_t) {holder}Size(const {holder}* self)"))
            cppWriter.WriteLine($"return self == nullptr ? 0 : reinterpret_cast<const {cppType}*>(self)->size();");
        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(bool) {holder}Add({holder}* self, {cValue} value)"))
        {
            cppWriter.WriteLine("if (self == nullptr) return false;");
            cppWriter.WriteLine($"return reinterpret_cast<{cppType}*>(self)->insert({ConvertCToCpp(value, "value")}).second;");
        }

        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(bool) {holder}TryGetAt(const {holder}* self, size_t index, {cValue}* out_value)"))
        {
            cppWriter.WriteLine($"if (self == nullptr || index >= reinterpret_cast<const {cppType}*>(self)->size()) return false;");
            cppWriter.WriteLine($"auto iterator = reinterpret_cast<const {cppType}*>(self)->begin();");
            cppWriter.WriteLine("std::advance(iterator, index);");
            cppWriter.WriteLine($"if (out_value != nullptr) *out_value = {ConvertCppToC(value, "*iterator")};");
            cppWriter.WriteLine("return true;");
        }
    }

    private void WriteVariantSupport(
        CppType type,
        string holder,
        string cppType,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        IReadOnlyList<CppType> alternatives = this.m_config.GetTemplateTypeArguments(type);
        headerWriter.WriteLine($"{this.m_config.namePrefix}API(size_t) {holder}Index(const {holder}* self);");
        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(size_t) {holder}Index(const {holder}* self)"))
            cppWriter.WriteLine($"return self == nullptr ? static_cast<size_t>(-1) : reinterpret_cast<const {cppType}*>(self)->index();");
        for (int index = 0; index < alternatives.Count; index++)
        {
            CppType alternative = alternatives[index];
            string cType = this.m_config.GetCType(alternative);
            headerWriter.WriteLine($"{this.m_config.namePrefix}API({holder}*) {holder}Create{index}({cType} value);");
            headerWriter.WriteLine($"{this.m_config.namePrefix}API(bool) {holder}TryGet{index}(const {holder}* self, {cType}* out_value);");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({holder}*) {holder}Create{index}({cType} value)"))
                cppWriter.WriteLine($"return reinterpret_cast<{holder}*>(new {cppType}(std::in_place_index<{index}>, {ConvertCToCpp(alternative, "value")}));");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(bool) {holder}TryGet{index}(const {holder}* self, {cType}* out_value)"))
            {
                cppWriter.WriteLine("if (self == nullptr) return false;");
                cppWriter.WriteLine($"auto* value = std::get_if<{index}>(reinterpret_cast<const {cppType}*>(self));");
                cppWriter.WriteLine("if (value == nullptr) return false;");
                cppWriter.WriteLine($"if (out_value != nullptr) *out_value = {ConvertCppToC(alternative, "*value")};");
                cppWriter.WriteLine("return true;");
            }
        }
    }

    private void WriteExpectedSupport(
        CppType type,
        string holder,
        string cppType,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        IReadOnlyList<CppType> arguments = this.m_config.GetTemplateTypeArguments(type);
        CppType value = arguments[0];
        CppType error = arguments[1];
        string cValue = this.m_config.GetCType(value);
        string cError = this.m_config.GetCType(error);
        headerWriter.WriteLine($"{this.m_config.namePrefix}API({holder}*) {holder}CreateValue({cValue} value);");
        headerWriter.WriteLine($"{this.m_config.namePrefix}API({holder}*) {holder}CreateError({cError} error);");
        headerWriter.WriteLine($"{this.m_config.namePrefix}API(bool) {holder}HasValue(const {holder}* self);");
        headerWriter.WriteLine($"{this.m_config.namePrefix}API(bool) {holder}TryGetValue(const {holder}* self, {cValue}* out_value);");
        headerWriter.WriteLine($"{this.m_config.namePrefix}API(bool) {holder}TryGetError(const {holder}* self, {cError}* out_error);");
        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({holder}*) {holder}CreateValue({cValue} value)"))
            cppWriter.WriteLine($"return reinterpret_cast<{holder}*>(new {cppType}({ConvertCToCpp(value, "value")}));");
        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({holder}*) {holder}CreateError({cError} error)"))
            cppWriter.WriteLine($"return reinterpret_cast<{holder}*>(new {cppType}(std::unexpected({ConvertCToCpp(error, "error")})));");
        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(bool) {holder}HasValue(const {holder}* self)"))
            cppWriter.WriteLine($"return self != nullptr && reinterpret_cast<const {cppType}*>(self)->has_value();");
        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(bool) {holder}TryGetValue(const {holder}* self, {cValue}* out_value)"))
        {
            cppWriter.WriteLine($"if (self == nullptr || !reinterpret_cast<const {cppType}*>(self)->has_value()) return false;");
            cppWriter.WriteLine($"if (out_value != nullptr) *out_value = {ConvertCppToC(value, "reinterpret_cast<const " + cppType + "*>(self)->value()")};");
            cppWriter.WriteLine("return true;");
        }

        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(bool) {holder}TryGetError(const {holder}* self, {cError}* out_error)"))
        {
            cppWriter.WriteLine($"if (self == nullptr || reinterpret_cast<const {cppType}*>(self)->has_value()) return false;");
            cppWriter.WriteLine($"if (out_error != nullptr) *out_error = {ConvertCppToC(error, "reinterpret_cast<const " + cppType + "*>(self)->error()")};");
            cppWriter.WriteLine("return true;");
        }
    }

    private bool IsOpaqueValueType(CppType type) => this.m_config.IsMapType(type) || this.m_config.IsSetType(type) || this.m_config.IsVariantType(type) || this.m_config.IsExpectedType(type);
    private bool IsContiguousCollection(CppType type) => this.m_config.IsSpanType(type) || this.m_config.IsVectorType(type) || this.m_config.IsArrayType(type);
    private string ConvertCToCpp(
        CppType type,
        string expression
    ) {
        CppType current = type;
        while (current is CppQualifiedType qualified)
            current = qualified.elementType;
        while (current is CppTypedef typedef)
            current = typedef.elementType;
        return current switch
        {
            CppEnum cppEnum => $"static_cast<{cppEnum.fullName}>({expression})",
            CppPointerType => $"reinterpret_cast<{GetOriginalCppTypeName(type)}>({expression})",
            _ => expression
        };
    }

    private string ConvertCppToC(
        CppType type,
        string expression
    ) => GetCppReturnExpression(type, this.m_config.GetCType(type), expression);
    private void WriteClasses(
        IEnumerable<CppClass> classes,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        List<CppClass> sourceClasses = classes.Where(IsSupportedClass).ToList();
        foreach (CppClass cppClass in sourceClasses.Where(RequiresOpaqueBridge))
        {
            string typeName = this.m_config.GetCTypeName(cppClass);
            headerWriter.WriteLine($"typedef struct {typeName} {typeName};");
        }

        if (sourceClasses.Any(RequiresOpaqueBridge))
        {
            headerWriter.WriteLine();
        }

        foreach (var c in sourceClasses)
        {
            if (RequiresOpaqueBridge(c))
            {
                WriteClass(c, headerWriter, cppWriter);
            }
            else
            {
                WriteDto(c, headerWriter, cppWriter);
            }
        }
    }

    private bool IsSupportedClass(CppClass cppClass)
    {
        return CppBridgeDeclarationPolicy.IsAccessible(cppClass)
            && this.m_config.ResolveTypeLowering(cppClass, CppTypeLoweringUse.Field) == null
            && (cppClass.classKind is CppClassKind.Class or CppClassKind.Struct)
            && cppClass.templateKind != CppTemplateKind.TemplateClass
            && cppClass.sourceFile != null && cppClass.isDefinition;
    }

    private static bool RequiresOpaqueBridge(CppClass cppClass)
    {
        return CppBridgeDeclarationPolicy.RequiresOpaqueBridge(cppClass);
    }

    private static List<CppFunction> GetBridgeFunctions(CppClass cppClass)
    {
        return cppClass.functions.Count > 0 ? cppClass.functions.ToList() : cppClass.specializedTemplate?.functions.ToList() ?? [];
    }

    private static List<CppFunction> GetBridgeConstructors(CppClass cppClass)
    {
        return cppClass.constructors.Count > 0 ? cppClass.constructors.ToList() : cppClass.specializedTemplate?.constructors.ToList() ?? [];
    }

    private string GetSpecializedCType(
        CppClass cppClass,
        CppType type
    ) {
        if (this.m_config.IsUtf8StringType(type))
            return "const char*";
        string? parameterName = type switch
        {
            CppTemplateParameterType parameter => parameter.name,
            CppUnexposedType unexposed => unexposed.name,
            _ => null
        };
        if (parameterName != null && cppClass.specializedTemplate != null)
        {
            int index = cppClass.specializedTemplate.templateParameters.ToList().FindIndex(candidate => candidate is CppTemplateParameterType templateParameter && templateParameter.name == parameterName);
            if (index >= 0 && index < cppClass.templateSpecializedArguments.Count && cppClass.templateSpecializedArguments[index].argAsType is CppType argumentType)
                return this.m_config.GetCType(argumentType);
        }

        return type switch
        {
            CppPointerType pointer => GetSpecializedCType(cppClass, pointer.elementType) + "*",
            CppReferenceType reference => GetSpecializedCType(cppClass, reference.elementType) + "*",
            CppQualifiedType qualified => (qualified.qualifier == CppTypeQualifier.Const ? "const " : qualified.qualifier == CppTypeQualifier.Volatile ? "volatile " : string.Empty) + GetSpecializedCType(cppClass, qualified.elementType),
            CppArrayType array => GetSpecializedCType(cppClass, array.elementType) + "*",
            CppTemplateArgument { argAsType: not null } argument => this.m_config.GetCType(argument.argAsType),
            _ => this.m_config.GetCType(type)
        };
    }

    private void WriteDto(
        CppClass cppClass,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        headerWriter.BeginBlock($"typedef struct");
        if (cppClass.baseTypes.Any())
        {
            Stack<(CppClass cls, bool close)> stack = new();
            stack.Push((cppClass, false));
            while (stack.Count > 0)
            {
                var pair = stack.Pop();
                if (pair.close)
                {
                    WriteFields(headerWriter, pair.cls.fields);
                }
                else
                {
                    stack.Push((pair.cls, true));
                    foreach (var baseType in pair.cls.baseTypes)
                    {
                        if (baseType.type is CppClass baseClass)
                        {
                            stack.Push((baseClass, false));
                        }
                    }
                }
            }
        }
        else
        {
            WriteFields(headerWriter, cppClass.fields);
        }

        headerWriter.EndBlock($"}} {this.m_config.GetCTypeName(cppClass)};");
    }

    private void WriteClass(
        CppClass cppClass,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        var typeName = this.m_config.GetCTypeName(cppClass);
        WriteVirtualCallbackProxy(cppClass, typeName, headerWriter, cppWriter);
        WriteConstructors(cppClass, typeName, headerWriter, cppWriter);
        WriteInheritanceCasts(cppClass, typeName, headerWriter, cppWriter);
        CppFunction? callableDestructor = cppClass.destructors.FirstOrDefault(destructor => (destructor.visibility is CppVisibility.Public or CppVisibility.Default) && !destructor.flags.HasFlag(CppFunctionFlags.Deleted));
        bool canDestroy = cppClass.destructors.Count == 0 || callableDestructor != null;
        if (canDestroy)
        {
            string defaultDestroyName = typeName + "Destroy";
            if (callableDestructor != null && this.m_config.IsCallableExcluded(cppClass, callableDestructor, defaultDestroyName))
                canDestroy = false;
            string destroyName = callableDestructor == null ? defaultDestroyName : this.m_config.GetCFunctionName(cppClass, callableDestructor, defaultDestroyName);
            destroyName = GetAvailableFunctionName(destroyName);
            if (canDestroy)
            {
                this.m_definedFunctions.Add(destroyName);
                headerWriter.WriteLine($"{this.m_config.namePrefix}API(void) {destroyName}({typeName}* self);");
                using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL(void) {destroyName}({typeName}* self)"))
                {
                    WriteGuarded(cppWriter, "return;", writer =>
                    {
                        writer.WriteLine($"auto* ptr = reinterpret_cast<{cppClass.fullName}*>(self);");
                        writer.WriteLine("delete ptr;");
                    });
                }
            }
        }

        IReadOnlyList<CppFunction> functions = GetBridgeFunctions(cppClass);
        for (int j = 0; j < functions.Count; j++)
        {
            var f = functions[j];
            if (f.visibility != CppVisibility.Public || f.flags.HasFlag(CppFunctionFlags.Deleted))
            {
                continue;
            }

            string defaultName = $"{this.m_config.GetCTypeName(cppClass)}_{f.name}";
            if (this.m_config.IsCallableExcluded(cppClass, f, defaultName))
                continue;
            WriteFunctionH(cppClass, f, headerWriter);
            WriteFunctionCpp(cppClass, f, cppWriter);
        }
    }

    private void WriteVirtualCallbackProxy(
        CppClass cppClass,
        string typeName,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        if (!this.m_config.virtualCallbackInterfaces.Contains(cppClass.fullName, StringComparer.Ordinal) && !this.m_config.virtualCallbackInterfaces.Contains(cppClass.name, StringComparer.Ordinal))
            return;
        List<CppFunction> methods = GetBridgeFunctions(cppClass).Where(function => function.visibility == CppVisibility.Public && function.flags.HasFlag(CppFunctionFlags.Pure)).ToList();
        if (methods.Count == 0)
            throw new InvalidOperationException($"Configured virtual callback interface '{cppClass.fullName}' has no public pure virtual methods.");
        string callbacksType = typeName + "Callbacks";
        List<string> callbackFields = [];
        for (int i = 0; i < methods.Count; i++)
        {
            CppFunction method = methods[i];
            if (method.returnType is CppReferenceType || this.m_config.IsSpanType(method.returnType) || this.m_config.IsUniquePtrType(method.returnType))
                throw new NotSupportedException($"Virtual callback return type '{method.returnType}' is not supported for '{cppClass.fullName}::{method.name}'.");
            if (method.parameters.Any(parameter => this.m_config.IsSpanType(parameter.type) || this.m_config.IsUniquePtrType(parameter.type)))
                throw new NotSupportedException($"Virtual callback STL parameters require an explicit callback mapping for '{cppClass.fullName}::{method.name}'.");
            string callbackName = typeName + method.name + "Callback" + (i == 0 ? string.Empty : i.ToString());
            string parameters = string.Join(", ", new[] { "void* user_data" }.Concat(method.parameters.Select(parameter => $"{this.m_config.GetCType(parameter.type)} {parameter.name}")));
            headerWriter.WriteLine($"typedef {this.m_config.GetCType(method.returnType)} ({this.m_config.namePrefix}CALL *{callbackName})({parameters});");
            callbackFields.Add($"{callbackName} {method.name}{i};");
        }

        headerWriter.BeginBlock("typedef struct");
        foreach (string callbackField in callbackFields)
            headerWriter.WriteLine(callbackField);
        headerWriter.EndBlock($"}} {callbacksType};");
        headerWriter.WriteLine($"{this.m_config.namePrefix}API({typeName}*) {typeName}CreateProxy({callbacksType} callbacks, void* user_data);");
        string proxyType = typeName + "Proxy";
        using (cppWriter.PushBlock($"class {proxyType} final : public {cppClass.fullName}"))
        {
            cppWriter.WriteLine("public:");
            cppWriter.WriteLine($"{callbacksType} callbacks;");
            cppWriter.WriteLine("void* user_data;");
            cppWriter.WriteLine($"{proxyType}({callbacksType} callbacks, void* user_data) : callbacks(callbacks), user_data(user_data) {{}}");
            for (int i = 0; i < methods.Count; i++)
            {
                CppFunction method = methods[i];
                string qualifiers = method.isConst ? " const override" : " override";
                using (cppWriter.PushBlock($"{method.returnType.GetDisplayName()} {method.name}({GetCppFunctionSignature(method)}){qualifiers}"))
                {
                    string field = $"callbacks.{method.name}{i}";
                    bool returnsVoid = method.returnType is CppPrimitiveType { kind: CppPrimitiveKind.Void };
                    cppWriter.WriteLine($"if ({field} == nullptr) {(returnsVoid ? "return;" : "return {};")}");
                    List<string> arguments = ["user_data"];
                    arguments.AddRange(method.parameters.Select(parameter => parameter.type is CppReferenceType ? "&" + parameter.name : parameter.name));
                    string invocation = $"{field}({string.Join(", ", arguments)})";
                    cppWriter.WriteLine(returnsVoid ? invocation + ";" : "return " + invocation + ";");
                }
            }
        }

        cppWriter.WriteLine(";");
        using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({typeName}*) {typeName}CreateProxy({callbacksType} callbacks, void* user_data)"))
        {
            WriteGuarded(cppWriter, "return nullptr;", writer => writer.WriteLine($"return reinterpret_cast<{typeName}*>(new {proxyType}(callbacks, user_data));"));
        }
    }

    private void WriteInheritanceCasts(
        CppClass cppClass,
        string typeName,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        foreach (CppBaseType baseType in cppClass.baseTypes)
        {
            if (baseType.type is not CppClass baseClass)
                continue;
            string baseName = this.m_config.GetCTypeName(baseClass);
            string upcastName = typeName + "As" + baseName;
            headerWriter.WriteLine($"{this.m_config.namePrefix}API({baseName}*) {upcastName}({typeName}* self);");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({baseName}*) {upcastName}({typeName}* self)"))
            {
                cppWriter.WriteLine("if (self == nullptr) return nullptr;");
                cppWriter.WriteLine($"auto* derived = reinterpret_cast<{cppClass.fullName}*>(self);");
                cppWriter.WriteLine($"return reinterpret_cast<{baseName}*>(static_cast<{baseClass.fullName}*>(derived));");
            }

            if (!baseClass.HasVirtualMembers())
                continue;
            string downcastName = typeName + "From" + baseName;
            headerWriter.WriteLine($"{this.m_config.namePrefix}API({typeName}*) {downcastName}({baseName}* self);");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({typeName}*) {downcastName}({baseName}* self)"))
            {
                cppWriter.WriteLine("if (self == nullptr) return nullptr;");
                cppWriter.WriteLine($"auto* base_ptr = reinterpret_cast<{baseClass.fullName}*>(self);");
                cppWriter.WriteLine($"return reinterpret_cast<{typeName}*>(dynamic_cast<{cppClass.fullName}*>(base_ptr));");
            }
        }
    }

    private void WriteFreeFunctions(
        IEnumerable<CppFunction> functions,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        foreach (CppFunction function in functions)
        {
            if (function.isExternC || function.flags.HasFlag(CppFunctionFlags.FunctionTemplate) || function.templateParameters.Count > 0 || function.visibility is not (CppVisibility.Public or CppVisibility.Default))
                continue;
            string baseName = this.m_config.GetCFunctionName(function);
            if (this.m_config.IsCallableExcluded(null, function, baseName))
                continue;
            string name = baseName;
            int suffix = 1;
            while (!this.m_definedFunctions.Add(name))
                name = baseName + suffix++;
            bool returnsCollection = IsContiguousCollection(function.returnType);
            bool returnsOptional = this.m_config.IsOptionalType(function.returnType);
            string cReturnType = returnsOptional ? "bool" : this.m_config.GetCType(function.returnType);
            string cSignature = GetCParameterSignature(function.parameters);
            if (returnsCollection)
                cSignature = cSignature == "void" ? "size_t* out_count" : cSignature + ", size_t* out_count";
            if (returnsOptional)
            {
                if (!this.m_config.TryGetTemplateElementType(function.returnType, out CppType? optionalElement))
                    throw new NotSupportedException($"Unable to resolve optional return '{function.returnType}'.");
                string output = this.m_config.GetCType(function.returnType) + "* out_value";
                cSignature = cSignature == "void" ? output : cSignature + ", " + output;
            }

            string arguments = GetCppFunctionSignatureTypeless(function);
            headerWriter.WriteLine($"{this.m_config.namePrefix}API({cReturnType}) {name}({cSignature});");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({cReturnType}) {name}({cSignature})"))
            {
                bool returnsVoid = function.returnType is CppPrimitiveType { kind: CppPrimitiveKind.Void };
                string failureStatement = returnsVoid ? "return;" : cReturnType.EndsWith('*') ? "return nullptr;" : "return {};";
                WriteGuarded(cppWriter, failureStatement, writer =>
                {
                    string qualifiedName = string.IsNullOrEmpty(function.fullParentName) ? function.name : function.fullParentName + "::" + function.name;
                    string invocation = this.m_config.ApplyCallableInvocation(null, function, baseName, $"{qualifiedName}({arguments})");
                    if (returnsVoid)
                        writer.WriteLine(invocation + ";");
                    else if (returnsCollection)
                    {
                        if (this.m_config.IsVectorType(function.returnType) || this.m_config.IsArrayType(function.returnType))
                        {
                            writer.WriteLine($"thread_local {GetCppValueTypeName(function.returnType)} return_value;");
                            writer.WriteLine($"return_value = {invocation};");
                        }
                        else
                        {
                            writer.WriteLine($"auto return_value = {invocation};");
                        }

                        writer.WriteLine("if (out_count != nullptr) *out_count = return_value.size();");
                        writer.WriteLine("return return_value.data();");
                    }
                    else if (returnsOptional)
                    {
                        writer.WriteLine($"auto optional_result = {invocation};");
                        writer.WriteLine("if (!optional_result.has_value()) return false;");
                        this.m_config.TryGetTemplateElementType(function.returnType, out CppType? optionalElement);
                        if (this.m_config.IsBlittableBridgeType(optionalElement!))
                            writer.WriteLine("if (out_value != nullptr) *out_value = *optional_result;");
                        else
                            writer.WriteLine($"if (out_value != nullptr) *out_value = reinterpret_cast<{this.m_config.GetCType(optionalElement!)}*>(new {GetCppValueTypeName(optionalElement!)}(*optional_result));");
                        writer.WriteLine("return true;");
                    }
                    else if (IsOpaqueValueType(function.returnType))
                    {
                        string holder = this.m_config.GetOpaqueValueHolderName(function.returnType);
                        writer.WriteLine($"return reinterpret_cast<{holder}*>(new {GetCppValueTypeName(function.returnType)}({invocation}));");
                    }
                    else if (this.m_config.IsSharedPtrType(function.returnType))
                    {
                        writer.WriteLine($"return reinterpret_cast<{this.m_config.GetSharedPtrHolderName(function.returnType)}*>(new {GetCppValueTypeName(function.returnType)}({invocation}));");
                    }
                    else if (this.m_config.IsUniquePtrType(function.returnType))
                    {
                        writer.WriteLine($"return {invocation}.release();");
                    }
                    else if (this.m_config.IsPathType(function.returnType))
                    {
                        writer.WriteLine("thread_local std::string return_value;");
                        writer.WriteLine($"auto path_value = {invocation};");
                        writer.WriteLine("auto utf8_value = path_value.u8string();");
                        writer.WriteLine("return_value.assign(reinterpret_cast<const char*>(utf8_value.data()), utf8_value.size());");
                        writer.WriteLine("return return_value.c_str();");
                    }
                    else if (this.m_config.IsUtf8StringType(function.returnType))
                    {
                        writer.WriteLine($"thread_local {GetUtf8StringValueTypeName(function.returnType)} return_value;");
                        writer.WriteLine($"return_value = {invocation};");
                        writer.WriteLine("return return_value.c_str();");
                    }
                    else if (this.m_config.IsChronoDurationType(function.returnType))
                        writer.WriteLine($"return static_cast<int64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>({invocation}).count());");
                    else if (this.m_config.IsChronoTimePointType(function.returnType))
                        writer.WriteLine($"return static_cast<int64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(({invocation}).time_since_epoch()).count());");
                    else if (function.returnType is CppReferenceType)
                        writer.WriteLine($"return {GetCppReferenceReturnExpression(function.returnType, cReturnType, invocation)};");
                    else
                        writer.WriteLine($"return {GetCppReturnExpression(function.returnType, cReturnType, invocation)};");
                });
            }
        }
    }

    private string GetCParameterSignature(IEnumerable<CppParameter> parameters)
    {
        string signature = string.Join(", ", parameters.SelectMany(parameter => GetCParameterDeclarations(null, parameter)));
        return string.IsNullOrEmpty(signature) ? "void" : signature;
    }

    private void WriteGuarded(
        ICodeWriter writer,
        string failureStatement,
        Action<ICodeWriter> body
    ) {
        using (writer.PushBlock("try"))
        {
            writer.WriteLine($"{this.m_config.errorSymbolPrefix}ClearLastError();");
            body(writer);
        }

        using (writer.PushBlock("catch (const std::exception& exception)"))
        {
            writer.WriteLine($"{this.m_config.errorSymbolPrefix}last_error = exception.what();");
            writer.WriteLine(failureStatement);
        }

        using (writer.PushBlock("catch (...)"))
        {
            writer.WriteLine($"{this.m_config.errorSymbolPrefix}last_error = \"Unknown C++ exception\";");
            writer.WriteLine(failureStatement);
        }
    }

    private void WriteConstructors(
        CppClass cppClass,
        string typeName,
        ICodeWriter headerWriter,
        ICodeWriter cppWriter
    ) {
        if (GetBridgeFunctions(cppClass).Any(function => function.flags.HasFlag(CppFunctionFlags.Pure)))
        {
            return;
        }

        IReadOnlyList<CppFunction> availableConstructors = GetBridgeConstructors(cppClass);
        List<CppFunction?> constructors = availableConstructors.Count == 0 ? [null] : availableConstructors.Where(constructor => (constructor.visibility is CppVisibility.Public or CppVisibility.Default) && !constructor.flags.HasFlag(CppFunctionFlags.Deleted)).Cast<CppFunction?>().ToList();
        for (int i = 0; i < constructors.Count; i++)
        {
            CppFunction? constructor = constructors[i];
            string defaultName = typeName + "Create" + (i == 0 ? string.Empty : i.ToString());
            if (constructor != null && this.m_config.IsCallableExcluded(cppClass, constructor, defaultName))
                continue;
            string name = constructor == null ? defaultName : this.m_config.GetCFunctionName(cppClass, constructor, defaultName);
            name = GetAvailableFunctionName(name);
            this.m_definedFunctions.Add(name);
            string cSignature = constructor == null ? "void" : GetCParameterSignature(cppClass, constructor.parameters);
            string arguments = constructor == null ? string.Empty : GetCppFunctionSignatureTypeless(constructor);
            headerWriter.WriteLine($"{this.m_config.namePrefix}API({typeName}*) {name}({cSignature});");
            using (cppWriter.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({typeName}*) {name}({cSignature})"))
            {
                WriteGuarded(cppWriter, "return nullptr;", writer => writer.WriteLine($"return reinterpret_cast<{typeName}*>(new {cppClass.fullName}({arguments}));"));
            }
        }
    }

    private string GetCParameterSignature(
        CppClass cppClass,
        IEnumerable<CppParameter> parameters
    ) {
        string signature = string.Join(", ", parameters.SelectMany(parameter => GetCParameterDeclarations(cppClass, parameter)));
        return string.IsNullOrEmpty(signature) ? "void" : signature;
    }

    private IEnumerable<string> GetCParameterDeclarations(
        CppClass? cppClass,
        CppParameter parameter
    ) {
        CppTypeLoweringPlan? lowering = this.m_config.ResolveTypeLowering(parameter.type, CppTypeLoweringUse.Parameter);
        if (lowering?.abiParameters.Count > 0)
        {
            foreach (CppAbiParameter abiParameter in lowering.abiParameters)
                yield return $"{abiParameter.cAbiType} {parameter.name}{abiParameter.nameSuffix}";
            yield break;
        }

        string type = cppClass == null ? this.m_config.GetCType(parameter.type) : GetSpecializedCType(cppClass, parameter.type);
        yield return $"{type} {parameter.name}";
        if (IsContiguousCollection(parameter.type))
            yield return $"size_t {parameter.name}_count";
        else if (this.m_config.IsOptionalType(parameter.type))
            yield return $"bool {parameter.name}_has_value";
    }

    private void WriteFields(
        ICodeWriter writer,
        IEnumerable<CppField> fields
    ) {
        foreach (var field in fields)
        {
            if ((field.storageQualifier & CppStorageQualifier.Static) != 0)
            {
                continue;
            }

            writer.WriteLine($"{this.m_config.GetCType(field.type)} {field.name};");
        }
    }

    private string GetCFunctionSignature(
        CppClass c,
        CppFunction f
    ) {
        StringBuilder sb = new();
        bool isStatic = (f.storageQualifier & CppStorageQualifier.Static) != 0;
        if (!isStatic)
        {
            sb.Append($"{this.m_config.GetCType(c)}* self");
        }

        foreach (CppParameter parameter in f.parameters)
        {
            foreach (string declaration in GetCParameterDeclarations(c, parameter))
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(declaration);
            }
        }

        if (IsContiguousCollection(f.returnType))
        {
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append("size_t* out_count");
        }

        if (this.m_config.IsOptionalType(f.returnType))
        {
            if (!this.m_config.TryGetTemplateElementType(f.returnType, out CppType? optionalElement))
                throw new NotSupportedException($"Unable to resolve optional return '{f.returnType}'.");
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append($"{this.m_config.GetCType(f.returnType)}* out_value");
        }

        return sb.Length == 0 ? "void" : sb.ToString();
    }

    private static string GetCppFunctionSignature(CppFunction f)
    {
        StringBuilder sb = new();
        for (int i = 0; i < f.parameters.Count; i++)
        {
            var param = f.parameters[i];
            sb.Append($"{param.type} {param.name}");
            if (i < f.parameters.Count - 1)
            {
                sb.Append(", ");
            }
        }

        return sb.ToString();
    }

    private string GetCppValueTypeName(CppType type) => this.m_config.GetCppValueTypeSpelling(type);
    private string GetUtf8StringValueTypeName(CppType type) => this.m_config.GetCppValueTypeSpelling(type);
    private static string GetCppReferenceReturnExpression(
        CppType type,
        string cReturnType,
        string invocation
    ) {
        CppType current = type is CppReferenceType reference ? reference.elementType : type;
        while (current is CppQualifiedType qualified)
            current = qualified.elementType;
        return current is CppPrimitiveType ? $"&({invocation})" : $"reinterpret_cast<{cReturnType}>(&({invocation}))";
    }

    private string GetCppReturnExpression(
        CppType type,
        string cReturnType,
        string invocation
    ) {
        CppTypeLoweringPlan? lowering = this.m_config.ResolveTypeLowering(type, CppTypeLoweringUse.Return);
        if (!string.IsNullOrWhiteSpace(lowering?.returnToCExpression))
            return RenderTypeExpression(lowering.returnToCExpression!, type, invocation);
        if (Cpp2CGeneratorConfig.IsFunctionPointerType(type))
            return $"reinterpret_cast<{cReturnType}>({invocation})";
        CppType current = type;
        while (current is CppQualifiedType qualified)
            current = qualified.elementType;
        while (current is CppTypedef typedef)
            current = typedef.elementType;
        if (current is CppEnum)
            return $"static_cast<{cReturnType}>({invocation})";
        if (current is CppPointerType)
            return $"reinterpret_cast<{cReturnType}>({invocation})";
        return invocation;
    }

    private string RenderTypeExpression(
        string template,
        CppType type,
        string value
    ) {
        string name = value.All(character => char.IsLetterOrDigit(character) || character == '_') ? value : "value";
        return template.Replace("{value}", value, StringComparison.Ordinal).Replace("{name}", name, StringComparison.Ordinal).Replace("{count}", name + "_count", StringComparison.Ordinal).Replace("{cppType}", GetCppValueTypeName(type), StringComparison.Ordinal);
    }

    private IReadOnlyList<string> GetRequiredLoweringHeaders(IEnumerable<CppFunction> functions)
    {
        SortedSet<string> headers = new(StringComparer.Ordinal);
        foreach (CppFunction function in functions)
        {
            CppTypeLoweringPlan? returnPlan = this.m_config.ResolveTypeLowering(function.returnType, CppTypeLoweringUse.Return);
            if (returnPlan != null)
                headers.UnionWith(returnPlan.requiredHeaders);
            foreach (CppParameter parameter in function.parameters)
            {
                CppTypeLoweringPlan? parameterPlan = this.m_config.ResolveTypeLowering(parameter.type, CppTypeLoweringUse.Parameter);
                if (parameterPlan != null)
                    headers.UnionWith(parameterPlan.requiredHeaders);
            }

            string defaultName = string.IsNullOrEmpty(function.fullParentName) ? this.m_config.GetCFunctionName(function) : $"{function.fullParentName}_{function.name}";
            CppCallableLoweringPlan? callable = this.m_config.ResolveCallableLowering(function.parent as CppClass, function, defaultName);
            if (callable != null)
                headers.UnionWith(callable.requiredHeaders);
        }

        return headers.ToArray();
    }

    private string GetOriginalCppTypeName(CppType type)
    {
        return type switch
        {
            CppPointerType pointer => GetOriginalCppTypeName(pointer.elementType) + "*",
            CppReferenceType reference => GetOriginalCppTypeName(reference.elementType) + "&",
            CppQualifiedType qualified => (qualified.qualifier == CppTypeQualifier.Const ? "const " : "volatile ") + GetOriginalCppTypeName(qualified.elementType),
            CppClass cppClass => cppClass.fullName,
            CppEnum cppEnum => cppEnum.fullName,
            CppTypedef typedef when !string.IsNullOrEmpty(typedef.fullParentName) => typedef.fullParentName + "::" + typedef.name,
            CppUnexposedType unexposed when this.m_cppQualifiedTypeNames.TryGetValue(unexposed.name, out string? fullName) => fullName,
            _ => type.GetDisplayName()
        };
    }

    private string GetCppArgumentExpression(CppParameter parameter)
    {
        CppType type = parameter.type;
        CppTypeLoweringPlan? lowering = this.m_config.ResolveTypeLowering(type, CppTypeLoweringUse.Parameter);
        if (!string.IsNullOrWhiteSpace(lowering?.parameterToCppExpression))
            return RenderTypeExpression(lowering.parameterToCppExpression!, type, parameter.name);
        CppType current = type;
        while (current is CppQualifiedType qualified)
            current = qualified.elementType;
        if (current is CppTypedef typedef && Cpp2CGeneratorConfig.IsFunctionPointerType(typedef))
            return $"reinterpret_cast<{GetCppValueTypeName(typedef)}>({parameter.name})";
        while (current is CppTypedef nested)
            current = nested.elementType;
        if (current is CppEnum cppEnum)
            return $"static_cast<{cppEnum.fullName}>({parameter.name})";
        if (current is CppReferenceType reference)
        {
            CppType element = reference.elementType;
            while (element is CppQualifiedType qualifiedElement)
                element = qualifiedElement.elementType;
            if (element is CppPrimitiveType)
                return "*" + parameter.name;
            return $"*reinterpret_cast<{GetOriginalCppTypeName(reference.elementType)}*>({parameter.name})";
        }

        if (current is CppPointerType pointer && pointer.elementType is not CppPrimitiveType { kind: CppPrimitiveKind.Void })
            return $"reinterpret_cast<{GetOriginalCppTypeName(type)}>({parameter.name})";
        return parameter.name;
    }

    private string GetCppFunctionSignatureTypeless(CppFunction f)
    {
        StringBuilder sb = new();
        for (int i = 0; i < f.parameters.Count; i++)
        {
            var param = f.parameters[i];
            if (this.m_config.IsOptionalType(param.type))
            {
                if (!this.m_config.TryGetTemplateElementType(param.type, out CppType? elementType))
                    throw new NotSupportedException($"Unable to resolve optional parameter '{param.type}'.");
                string optionalType = this.m_config.optionalTypes[0] + "<" + GetCppValueTypeName(elementType!) + ">";
                string value = this.m_config.IsBlittableBridgeType(elementType!) ? param.name : $"*reinterpret_cast<{GetCppValueTypeName(elementType!)}*>({param.name})";
                sb.Append($"{param.name}_has_value ? {optionalType}({value}) : std::nullopt");
            }
            else if (this.m_config.IsArrayType(param.type))
            {
                if (!this.m_config.TryGetTemplateElementType(param.type, out CppType? elementType))
                    throw new NotSupportedException($"Unable to resolve array parameter '{param.type}'.");
                long elementCount = this.m_config.GetArrayElementCount(param.type);
                string elementName = GetCppValueTypeName(elementType!);
                string arrayType = GetCppValueTypeName(param.type);
                sb.Append($"([&]() {{ if ({param.name}_count != {elementCount} || ({elementCount} != 0 && {param.name} == nullptr)) throw std::invalid_argument(\"Invalid fixed array extent\"); {arrayType} converted{{}}; std::copy_n(reinterpret_cast<const {elementName}*>({param.name}), {elementCount}, converted.begin()); return converted; }}())");
            }
            else if (this.m_config.IsVectorType(param.type))
            {
                if (!this.m_config.TryGetTemplateElementType(param.type, out CppType? elementType))
                    throw new NotSupportedException($"Unable to resolve vector parameter '{param.type}'.");
                string elementName = GetCppValueTypeName(elementType!);
                string vectorType = this.m_config.vectorTypes[0] + "<" + elementName + ">";
                sb.Append($"{vectorType}(reinterpret_cast<{elementName}*>({param.name}), reinterpret_cast<{elementName}*>({param.name}) + {param.name}_count)");
            }
            else if (this.m_config.IsSpanType(param.type))
            {
                if (!this.m_config.TryGetTemplateElementType(param.type, out CppType? elementType))
                    throw new NotSupportedException($"Unable to resolve span parameter '{param.type}'.");
                string spanType = this.m_config.spanTypes[0] + "<" + GetCppValueTypeName(elementType!) + ">";
                sb.Append($"{spanType}(reinterpret_cast<{GetCppValueTypeName(elementType!)}*>({param.name}), {param.name}_count)");
            }
            else if (this.m_config.IsSharedPtrType(param.type))
            {
                if (!this.m_config.TryGetTemplateElementType(param.type, out CppType? elementType))
                    throw new NotSupportedException($"Unable to resolve shared_ptr parameter '{param.type}'.");
                sb.Append($"*reinterpret_cast<{GetCppValueTypeName(param.type)}*>({param.name})");
            }
            else if (this.m_config.IsUniquePtrType(param.type))
            {
                if (!this.m_config.TryGetTemplateElementType(param.type, out CppType? elementType))
                    throw new NotSupportedException($"Unable to resolve unique_ptr parameter '{param.type}'.");
                sb.Append($"{GetCppValueTypeName(param.type)}(reinterpret_cast<{GetCppValueTypeName(elementType!)}*>({param.name}))");
            }
            else if (IsOpaqueValueType(param.type))
            {
                sb.Append($"*reinterpret_cast<{GetCppValueTypeName(param.type)}*>({param.name})");
            }
            else if (this.m_config.IsPathType(param.type))
            {
                sb.Append($"({param.name} == nullptr ? std::filesystem::path() : std::filesystem::path(std::u8string(reinterpret_cast<const char8_t*>({param.name}))))");
            }
            else if (this.m_config.IsChronoDurationType(param.type))
            {
                string typeName = GetCppValueTypeName(param.type);
                sb.Append($"std::chrono::duration_cast<{typeName}>(std::chrono::nanoseconds({param.name}))");
            }
            else if (this.m_config.IsChronoTimePointType(param.type))
            {
                string typeName = GetCppValueTypeName(param.type);
                sb.Append($"{typeName}(std::chrono::duration_cast<{typeName}::duration>(std::chrono::nanoseconds({param.name})))");
            }
            else if (this.m_config.IsUtf8StringType(param.type))
            {
                string typeName = GetUtf8StringValueTypeName(param.type);
                sb.Append($"{typeName}({param.name} == nullptr ? \"\" : {param.name})");
            }
            else
            {
                sb.Append(GetCppArgumentExpression(param));
            }

            if (i < f.parameters.Count - 1)
            {
                sb.Append(", ");
            }
        }

        return sb.ToString();
    }

    private string GetUniqueCFunctionName(
        CppClass c,
        CppFunction f
    ) {
        string defaultName = $"{this.m_config.GetCTypeName(c)}_{f.name}";
        string cName = this.m_config.GetCFunctionName(c, f, defaultName);
        return GetAvailableFunctionName(cName);
    }

    private string GetAvailableFunctionName(string requestedName)
    {
        int suffix = 1;
        string currentName = requestedName;
        while (this.m_definedFunctions.Contains(currentName))
            currentName = requestedName + suffix++;
        return currentName;
    }

    private void WriteFunctionH(
        CppClass c,
        CppFunction f,
        ICodeWriter writer
    ) {
        string name = GetUniqueCFunctionName(c, f);
        string cSignature = GetCFunctionSignature(c, f);
        string cReturnType = this.m_config.IsOptionalType(f.returnType) ? "bool" : GetSpecializedCType(c, f.returnType);
        this.m_definedFunctions.Add(name);
        this.m_mapping.Add((c, f), name);
        writer.WriteLine($"{this.m_config.namePrefix}API({cReturnType}) {name}({cSignature});");
    }

    private void WriteFunctionCpp(
        CppClass c,
        CppFunction f,
        ICodeWriter writer
    ) {
        var name = this.m_mapping[(c, f)];
        string cSignature = GetCFunctionSignature(c, f);
        string signature = GetCppFunctionSignatureTypeless(f);
        bool returnsCollection = IsContiguousCollection(f.returnType);
        string cReturnType = this.m_config.IsOptionalType(f.returnType) ? "bool" : GetSpecializedCType(c, f.returnType);
        bool isStatic = (f.storageQualifier & CppStorageQualifier.Static) != 0;
        using (writer.PushBlock($"{this.m_config.namePrefix}API_INTERNAL({cReturnType}) {name}({cSignature})"))
        {
            bool returnsVoid = f.returnType is CppPrimitiveType { kind: CppPrimitiveKind.Void };
            string failureStatement = returnsVoid ? "return;" : cReturnType.EndsWith('*') ? "return nullptr;" : "return {};";
            WriteGuarded(writer, failureStatement, guardedWriter =>
            {
                if (!isStatic)
                {
                    guardedWriter.WriteLine($"auto* ptr = reinterpret_cast<{c.fullName}*>(self);");
                }

                string invocation = isStatic ? $"{c.fullName}::{f.name}({signature})" : $"ptr->{f.name}({signature})";
                invocation = this.m_config.ApplyCallableInvocation(c, f, $"{this.m_config.GetCTypeName(c)}_{f.name}", invocation);
                if (returnsVoid)
                {
                    guardedWriter.WriteLine($"{invocation};");
                }
                else if (returnsCollection)
                {
                    if (this.m_config.IsVectorType(f.returnType) || this.m_config.IsArrayType(f.returnType))
                    {
                        guardedWriter.WriteLine($"thread_local {GetCppValueTypeName(f.returnType)} return_value;");
                        guardedWriter.WriteLine($"return_value = {invocation};");
                    }
                    else
                    {
                        guardedWriter.WriteLine($"auto return_value = {invocation};");
                    }

                    guardedWriter.WriteLine("if (out_count != nullptr) *out_count = return_value.size();");
                    guardedWriter.WriteLine("return return_value.data();");
                }
                else if (this.m_config.IsOptionalType(f.returnType))
                {
                    guardedWriter.WriteLine($"auto optional_result = {invocation};");
                    guardedWriter.WriteLine("if (!optional_result.has_value()) return false;");
                    this.m_config.TryGetTemplateElementType(f.returnType, out CppType? optionalElement);
                    if (this.m_config.IsBlittableBridgeType(optionalElement!))
                        guardedWriter.WriteLine("if (out_value != nullptr) *out_value = *optional_result;");
                    else
                        guardedWriter.WriteLine($"if (out_value != nullptr) *out_value = reinterpret_cast<{this.m_config.GetCType(optionalElement!)}*>(new {GetCppValueTypeName(optionalElement!)}(*optional_result));");
                    guardedWriter.WriteLine("return true;");
                }
                else if (IsOpaqueValueType(f.returnType))
                {
                    string holder = this.m_config.GetOpaqueValueHolderName(f.returnType);
                    guardedWriter.WriteLine($"return reinterpret_cast<{holder}*>(new {GetCppValueTypeName(f.returnType)}({invocation}));");
                }
                else if (this.m_config.IsSharedPtrType(f.returnType))
                {
                    guardedWriter.WriteLine($"return reinterpret_cast<{this.m_config.GetSharedPtrHolderName(f.returnType)}*>(new {GetCppValueTypeName(f.returnType)}({invocation}));");
                }
                else if (this.m_config.IsUniquePtrType(f.returnType))
                {
                    guardedWriter.WriteLine($"return {invocation}.release();");
                }
                else if (this.m_config.IsPathType(f.returnType))
                {
                    guardedWriter.WriteLine("thread_local std::string return_value;");
                    guardedWriter.WriteLine($"auto path_value = {invocation};");
                    guardedWriter.WriteLine("auto utf8_value = path_value.u8string();");
                    guardedWriter.WriteLine("return_value.assign(reinterpret_cast<const char*>(utf8_value.data()), utf8_value.size());");
                    guardedWriter.WriteLine("return return_value.c_str();");
                }
                else if (this.m_config.IsUtf8StringType(f.returnType))
                {
                    guardedWriter.WriteLine($"thread_local {GetUtf8StringValueTypeName(f.returnType)} return_value;");
                    guardedWriter.WriteLine($"return_value = {invocation};");
                    guardedWriter.WriteLine("return return_value.c_str();");
                }
                else if (this.m_config.IsChronoDurationType(f.returnType))
                    guardedWriter.WriteLine($"return static_cast<int64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>({invocation}).count());");
                else if (this.m_config.IsChronoTimePointType(f.returnType))
                    guardedWriter.WriteLine($"return static_cast<int64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(({invocation}).time_since_epoch()).count());");
                else if (f.returnType is CppReferenceType)
                {
                    guardedWriter.WriteLine($"return {GetCppReferenceReturnExpression(f.returnType, cReturnType, invocation)};");
                }
                else
                {
                    guardedWriter.WriteLine($"return {GetCppReturnExpression(f.returnType, cReturnType, invocation)};");
                }
            });
        }
    }
}
