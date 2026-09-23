using BGCS.Core;
using BGCS.Cpp2C.Metadata;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using BGCS.Cpp2C.Adapters;
using System.Text;

namespace BGCS.Cpp2C.GenerationSteps;

/// <summary>
/// Defines the public class <c>ClassGenerationStep</c>.
/// </summary>
public class ClassGenerationStep : GenerationStep
    {
        private readonly HashSet<string> definedFunctions = [];
        private readonly HashSet<string> definedTypes = [];
        private List<CFunction> definedCFunctions = [];

        private readonly Dictionary<(CppClass, CppFunction), string> mapping = [];
        private readonly Dictionary<string, string> cppQualifiedTypeNames = new(StringComparer.Ordinal);

        private static readonly string templateHeader = @"
#ifndef {PREFIX}COMMON_H
#define {PREFIX}COMMON_H

#include <stdio.h>
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
#ifdef {PREFIX}BUILD_SHARED
#define {PREFIX}EXPORT __declspec(dllexport)
#else
#define {PREFIX}EXPORT __declspec(dllimport)
#endif
#elif defined(__GNUC__) || defined(__clang__)
#ifdef {PREFIX}BUILD_SHARED
#define {PREFIX}EXPORT __attribute__((visibility(""default"")))
#else
#define {PREFIX}EXPORT
#endif
#else
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
#define {PREFIX}API_INTERNAL(type) {PREFIX}EXTERN {PREFIX}EXPORT type {PREFIX}CALL

{PREFIX}API(const char*) {ERROR_PREFIX}GetLastError(void);
{PREFIX}API(void) {ERROR_PREFIX}ClearLastError(void);

#endif
";


        /// <summary>
        /// Initializes a new instance of <see cref="ClassGenerationStep"/>.
        /// </summary>
        public ClassGenerationStep(Cpp2CCodeGenerator generator, Cpp2CGeneratorConfig config) : base(generator, config)
        {
        }

        /// <summary>
        /// Gets <c>Name</c>.
        /// </summary>
        public override string Name { get; } = "Class Generation Step";

        /// <summary>
        /// Executes public operation <c>Configure</c>.
        /// </summary>
        public override void Configure(Cpp2CGeneratorConfig config)
        {
        }

        /// <summary>
        /// Executes public operation <c>CopyFromMetadata</c>.
        /// </summary>
        public override void CopyFromMetadata(Cpp2CGeneratorMetadata metadata)
        {
        }

        /// <summary>
        /// Executes public operation <c>CopyToMetadata</c>.
        /// </summary>
        public override void CopyToMetadata(Cpp2CGeneratorMetadata metadata)
        {
        }

        /// <summary>
        /// Executes public operation <c>Reset</c>.
        /// </summary>
        public override void Reset()
        {
        }

        /// <summary>
        /// Runs generation logic through <c>Generate</c>.
        /// </summary>
        public override void Generate(FileSet files, ParseResult result, string outputPath, Cpp2CGeneratorConfig config, Cpp2CGeneratorMetadata metadata)
        {
            var compilation = result.Compilation;
            WriteCommon(outputPath, config);

            string headerName = $"Classes.h";
            string filePathHeader = Path.Combine(outputPath, "include", headerName);
            string filePathCpp = Path.Combine(outputPath, "src", $"Classes.cpp");
            using var headerWriter = new CodeWriter(filePathHeader, IncludeBuilder.Create().AddInclude("common.h").AddInclude("enums.h").Build(), null);
            IncludeBuilder cppIncludes = IncludeBuilder.Create();
            cppIncludes.AddInclude(headerName)
                .AddSystemInclude("algorithm")
                .AddSystemInclude("exception")
                .AddSystemInclude("iterator")
                .AddSystemInclude("stdexcept")
                .AddSystemInclude("string")
                .AddSystemInclude("utility");
            foreach (string sourceFile in result.EntryFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                cppIncludes.AddInclude(Path.GetFileName(sourceFile));
            }
            string cppPreamble = $"#define {config.NamePrefix}BUILD_SHARED\n" + cppIncludes.Build();
            using var cppWriter = new CodeWriter(filePathCpp, cppPreamble, null);
            WriteErrorSupport(cppWriter);

            List<CppClass> allClasses = [.. compilation.Classes];
            foreach (var ns in compilation.EnumerateNamespaces())
                allClasses.AddRange(ns.Classes);
            cppQualifiedTypeNames.Clear();
            foreach (CppClass cppClass in allClasses)
                cppQualifiedTypeNames[cppClass.Name] = cppClass.FullName;
            List<CppClass> classes = allClasses.Where(cppClass => files.Contains(cppClass.SourceFile)).ToList();
            List<CppFunction> functions = compilation.Functions.Where(function => files.Contains(function.SourceFile)).ToList();
            foreach (var ns in compilation.EnumerateNamespaces())
                functions.AddRange(ns.Functions.Where(function => files.Contains(function.SourceFile)));
            List<CppFunction> allFunctions = [.. functions, .. classes.SelectMany(GetBridgeFunctions)];
            foreach (CppFunction function in allFunctions)
                RegisterSourceTypeSpellings(function);
            WriteReferencedOpaqueTypes(classes, allFunctions, headerWriter);
            WriteSharedPtrSupport(allFunctions, headerWriter, cppWriter);
            WriteOpaqueValueSupport(allFunctions, headerWriter, cppWriter);
            WriteClasses(classes, headerWriter, cppWriter);
            WriteFreeFunctions(functions, headerWriter, cppWriter);
        }

        private void RegisterSourceTypeSpellings(CppFunction function)
        {
            config.RegisterSourceTypeSpelling(function.ReturnType, GetTypeSpelling(function.Cursor.ResultType));
            foreach (CppParameter parameter in function.Parameters)
                config.RegisterSourceTypeSpelling(parameter.Type, GetTypeSpelling(parameter.Cursor.Type));
        }

        private static string GetTypeSpelling(ClangSharp.Interop.CXType type)
        {
            ClangSharp.Interop.CXString spelling = type.Spelling;
            try
            {
                return spelling.ToString();
            }
            finally
            {
                spelling.Dispose();
            }
        }

        private static void WriteCommon(string outputPath, Cpp2CGeneratorConfig config)
        {
            string filePathHeader = Path.Combine(outputPath, "include", "common.h");
            using var headerWriter = new CodeWriter(filePathHeader, "", null);
            headerWriter.Write(templateHeader.Replace("{PREFIX}", config.NamePrefix)
                .Replace("{ERROR_PREFIX}", config.ErrorSymbolPrefix));
        }

        private void WriteErrorSupport(ICodeWriter writer)
        {
            writer.WriteLine($"static thread_local std::string {config.ErrorSymbolPrefix}last_error;");
            writer.WriteLine();
            using (writer.PushBlock($"{config.NamePrefix}API_INTERNAL(const char*) {config.ErrorSymbolPrefix}GetLastError(void)"))
            {
                writer.WriteLine($"return {config.ErrorSymbolPrefix}last_error.c_str();");
            }
            using (writer.PushBlock($"{config.NamePrefix}API_INTERNAL(void) {config.ErrorSymbolPrefix}ClearLastError(void)"))
            {
                writer.WriteLine($"{config.ErrorSymbolPrefix}last_error.clear();");
            }
        }

        private void WriteReferencedOpaqueTypes(IEnumerable<CppClass> classes, IEnumerable<CppFunction> functions, ICodeWriter headerWriter)
        {
            IEnumerable<CppType> fieldTypes = classes.SelectMany(cppClass => cppClass.Fields.Select(field => field.Type));
            IEnumerable<CppType> functionTypes = functions.SelectMany(function =>
                new[] { function.ReturnType }.Concat(function.Parameters.Select(parameter => parameter.Type)));
            HashSet<string> generatedTypes = classes.Where(IsSupportedClass)
                .Select(config.GetCTypeName).ToHashSet(StringComparer.Ordinal);
            HashSet<string> declarations = [];
            foreach (CppType type in fieldTypes.Concat(functionTypes))
            {
                if (!TryGetPointedClass(type, out CppClass? cppClass))
                    continue;
                string name = config.GetCTypeName(cppClass!);
                if (!generatedTypes.Contains(name) && declarations.Add(name))
                    headerWriter.WriteLine($"typedef struct {name} {name};");
            }
            foreach (CppType type in fieldTypes.Concat(functionTypes))
            {
                string cType;
                try { cType = config.GetCType(type); }
                catch { continue; }
                if (!cType.EndsWith('*'))
                    continue;
                string name = cType.TrimEnd('*').Trim().Replace("const ", string.Empty, StringComparison.Ordinal);
                if (name is "void" or "char" or "wchar_t" or "bool" or "short" or "int" or "long" or "float" or "double" ||
                    name.StartsWith("uint", StringComparison.Ordinal) || name.StartsWith("int", StringComparison.Ordinal) ||
                    !name.StartsWith(config.NamePrefix, StringComparison.Ordinal) || generatedTypes.Contains(name))
                    continue;
                if (declarations.Add(name))
                    headerWriter.WriteLine($"typedef struct {name} {name};");
            }
            if (declarations.Count > 0)
                headerWriter.WriteLine();
        }

        private static bool TryGetPointedClass(CppType type, out CppClass? cppClass)
        {
            CppType current = type;
            bool indirect = false;
            while (true)
            {
                switch (current)
                {
                    case CppPointerType pointer:
                        indirect = true;
                        current = pointer.ElementType;
                        continue;
                    case CppReferenceType reference:
                        indirect = true;
                        current = reference.ElementType;
                        continue;
                    case CppQualifiedType qualified:
                        current = qualified.ElementType;
                        continue;
                    case CppTypedef typedef:
                        current = typedef.ElementType;
                        continue;
                    default:
                        cppClass = indirect ? current as CppClass : null;
                        return cppClass != null;
                }
            }
        }

        private void WriteSharedPtrSupport(IEnumerable<CppFunction> functions, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            Dictionary<string, CppType> sharedTypes = [];
            foreach (CppFunction function in functions)
            {
                IEnumerable<CppType> types = new[] { function.ReturnType }.Concat(function.Parameters.Select(parameter => parameter.Type));
                foreach (CppType type in types.Where(config.IsSharedPtrType))
                    sharedTypes.TryAdd(config.GetSharedPtrHolderName(type), type);
            }
            foreach ((string holder, CppType sharedType) in sharedTypes)
            {
                config.TryGetTemplateElementType(sharedType, out CppType? elementType);
                string elementCType = config.GetCType(elementType!);
                string sharedCppType = GetCppValueTypeName(sharedType);
                if (elementType is CppClass elementClass)
                {
                    string elementHandle = config.GetCTypeName(elementClass);
                    headerWriter.WriteLine($"typedef struct {elementHandle} {elementHandle};");
                }
                headerWriter.WriteLine($"typedef struct {holder} {holder};");
                headerWriter.WriteLine($"{config.NamePrefix}API({elementCType}*) {holder}Get({holder}* self);");
                headerWriter.WriteLine($"{config.NamePrefix}API({holder}*) {holder}Clone({holder}* self);");
                headerWriter.WriteLine($"{config.NamePrefix}API(void) {holder}Destroy({holder}* self);");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({elementCType}*) {holder}Get({holder}* self)"))
                    cppWriter.WriteLine($"return self == nullptr ? nullptr : reinterpret_cast<{sharedCppType}*>(self)->get();");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({holder}*) {holder}Clone({holder}* self)"))
                    cppWriter.WriteLine($"return self == nullptr ? nullptr : reinterpret_cast<{holder}*>(new {sharedCppType}(*reinterpret_cast<{sharedCppType}*>(self))); ");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(void) {holder}Destroy({holder}* self)"))
                    cppWriter.WriteLine($"delete reinterpret_cast<{sharedCppType}*>(self);");
            }
        }

        private void WriteOpaqueValueSupport(IEnumerable<CppFunction> functions, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            Dictionary<string, CppType> holderTypes = new(StringComparer.Ordinal);
            foreach (CppFunction function in functions)
            {
                foreach (CppType type in new[] { function.ReturnType }.Concat(function.Parameters.Select(parameter => parameter.Type)))
                {
                    if (IsOpaqueValueType(type))
                        holderTypes.TryAdd(config.GetOpaqueValueHolderName(type), type);
                }
            }

            foreach ((string holder, CppType type) in holderTypes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                config.ResolveTypeAdapter(type, CppTypeAdapterUse.Field);
                string cppType = GetCppValueTypeName(type);
                if (definedTypes.Add(holder))
                    headerWriter.WriteLine($"typedef struct {holder} {holder};");
                headerWriter.WriteLine($"{config.NamePrefix}API({holder}*) {holder}Create(void);");
                headerWriter.WriteLine($"{config.NamePrefix}API({holder}*) {holder}Clone(const {holder}* self);");
                headerWriter.WriteLine($"{config.NamePrefix}API(void) {holder}Destroy({holder}* self);");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({holder}*) {holder}Create(void)"))
                    cppWriter.WriteLine($"return reinterpret_cast<{holder}*>(new {cppType}());");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({holder}*) {holder}Clone(const {holder}* self)"))
                    cppWriter.WriteLine($"return self == nullptr ? nullptr : reinterpret_cast<{holder}*>(new {cppType}(*reinterpret_cast<const {cppType}*>(self)));");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(void) {holder}Destroy({holder}* self)"))
                    cppWriter.WriteLine($"delete reinterpret_cast<{cppType}*>(self);");

                if (config.IsMapType(type))
                    WriteMapSupport(type, holder, cppType, headerWriter, cppWriter);
                else if (config.IsSetType(type))
                    WriteSetSupport(type, holder, cppType, headerWriter, cppWriter);
                else if (config.IsVariantType(type))
                    WriteVariantSupport(type, holder, cppType, headerWriter, cppWriter);
                else if (config.IsExpectedType(type))
                    WriteExpectedSupport(type, holder, cppType, headerWriter, cppWriter);
                headerWriter.WriteLine();
            }
        }

        private void WriteMapSupport(CppType type, string holder, string cppType, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            IReadOnlyList<CppType> arguments = config.GetTemplateTypeArguments(type);
            CppType key = arguments[0];
            CppType value = arguments[1];
            string cKey = config.GetCType(key);
            string cValue = config.GetCType(value);
            headerWriter.WriteLine($"{config.NamePrefix}API(size_t) {holder}Size(const {holder}* self);");
            headerWriter.WriteLine($"{config.NamePrefix}API(bool) {holder}Insert({holder}* self, {cKey} key, {cValue} value);");
            headerWriter.WriteLine($"{config.NamePrefix}API(bool) {holder}TryGetAt(const {holder}* self, size_t index, {cKey}* out_key, {cValue}* out_value);");
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(size_t) {holder}Size(const {holder}* self)"))
                cppWriter.WriteLine($"return self == nullptr ? 0 : reinterpret_cast<const {cppType}*>(self)->size();");
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(bool) {holder}Insert({holder}* self, {cKey} key, {cValue} value)"))
            {
                cppWriter.WriteLine("if (self == nullptr) return false;");
                cppWriter.WriteLine($"return reinterpret_cast<{cppType}*>(self)->insert_or_assign({ConvertCToCpp(key, "key")}, {ConvertCToCpp(value, "value")}).second;");
            }
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(bool) {holder}TryGetAt(const {holder}* self, size_t index, {cKey}* out_key, {cValue}* out_value)"))
            {
                cppWriter.WriteLine($"if (self == nullptr || index >= reinterpret_cast<const {cppType}*>(self)->size()) return false;");
                cppWriter.WriteLine($"auto iterator = reinterpret_cast<const {cppType}*>(self)->begin();");
                cppWriter.WriteLine("std::advance(iterator, index);");
                cppWriter.WriteLine($"if (out_key != nullptr) *out_key = {ConvertCppToC(key, "iterator->first")};");
                cppWriter.WriteLine($"if (out_value != nullptr) *out_value = {ConvertCppToC(value, "iterator->second")};");
                cppWriter.WriteLine("return true;");
            }
        }

        private void WriteSetSupport(CppType type, string holder, string cppType, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            CppType value = config.GetTemplateTypeArguments(type)[0];
            string cValue = config.GetCType(value);
            headerWriter.WriteLine($"{config.NamePrefix}API(size_t) {holder}Size(const {holder}* self);");
            headerWriter.WriteLine($"{config.NamePrefix}API(bool) {holder}Add({holder}* self, {cValue} value);");
            headerWriter.WriteLine($"{config.NamePrefix}API(bool) {holder}TryGetAt(const {holder}* self, size_t index, {cValue}* out_value);");
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(size_t) {holder}Size(const {holder}* self)"))
                cppWriter.WriteLine($"return self == nullptr ? 0 : reinterpret_cast<const {cppType}*>(self)->size();");
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(bool) {holder}Add({holder}* self, {cValue} value)"))
            {
                cppWriter.WriteLine("if (self == nullptr) return false;");
                cppWriter.WriteLine($"return reinterpret_cast<{cppType}*>(self)->insert({ConvertCToCpp(value, "value")}).second;");
            }
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(bool) {holder}TryGetAt(const {holder}* self, size_t index, {cValue}* out_value)"))
            {
                cppWriter.WriteLine($"if (self == nullptr || index >= reinterpret_cast<const {cppType}*>(self)->size()) return false;");
                cppWriter.WriteLine($"auto iterator = reinterpret_cast<const {cppType}*>(self)->begin();");
                cppWriter.WriteLine("std::advance(iterator, index);");
                cppWriter.WriteLine($"if (out_value != nullptr) *out_value = {ConvertCppToC(value, "*iterator")};");
                cppWriter.WriteLine("return true;");
            }
        }

        private void WriteVariantSupport(CppType type, string holder, string cppType, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            IReadOnlyList<CppType> alternatives = config.GetTemplateTypeArguments(type);
            headerWriter.WriteLine($"{config.NamePrefix}API(size_t) {holder}Index(const {holder}* self);");
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(size_t) {holder}Index(const {holder}* self)"))
                cppWriter.WriteLine($"return self == nullptr ? static_cast<size_t>(-1) : reinterpret_cast<const {cppType}*>(self)->index();");
            for (int index = 0; index < alternatives.Count; index++)
            {
                CppType alternative = alternatives[index];
                string cType = config.GetCType(alternative);
                headerWriter.WriteLine($"{config.NamePrefix}API({holder}*) {holder}Create{index}({cType} value);");
                headerWriter.WriteLine($"{config.NamePrefix}API(bool) {holder}TryGet{index}(const {holder}* self, {cType}* out_value);");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({holder}*) {holder}Create{index}({cType} value)"))
                    cppWriter.WriteLine($"return reinterpret_cast<{holder}*>(new {cppType}(std::in_place_index<{index}>, {ConvertCToCpp(alternative, "value")}));");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(bool) {holder}TryGet{index}(const {holder}* self, {cType}* out_value)"))
                {
                    cppWriter.WriteLine("if (self == nullptr) return false;");
                    cppWriter.WriteLine($"auto* value = std::get_if<{index}>(reinterpret_cast<const {cppType}*>(self));");
                    cppWriter.WriteLine("if (value == nullptr) return false;");
                    cppWriter.WriteLine($"if (out_value != nullptr) *out_value = {ConvertCppToC(alternative, "*value")};");
                    cppWriter.WriteLine("return true;");
                }
            }
        }

        private void WriteExpectedSupport(CppType type, string holder, string cppType, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            IReadOnlyList<CppType> arguments = config.GetTemplateTypeArguments(type);
            CppType value = arguments[0];
            CppType error = arguments[1];
            string cValue = config.GetCType(value);
            string cError = config.GetCType(error);
            headerWriter.WriteLine($"{config.NamePrefix}API({holder}*) {holder}CreateValue({cValue} value);");
            headerWriter.WriteLine($"{config.NamePrefix}API({holder}*) {holder}CreateError({cError} error);");
            headerWriter.WriteLine($"{config.NamePrefix}API(bool) {holder}HasValue(const {holder}* self);");
            headerWriter.WriteLine($"{config.NamePrefix}API(bool) {holder}TryGetValue(const {holder}* self, {cValue}* out_value);");
            headerWriter.WriteLine($"{config.NamePrefix}API(bool) {holder}TryGetError(const {holder}* self, {cError}* out_error);");
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({holder}*) {holder}CreateValue({cValue} value)"))
                cppWriter.WriteLine($"return reinterpret_cast<{holder}*>(new {cppType}({ConvertCToCpp(value, "value")}));");
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({holder}*) {holder}CreateError({cError} error)"))
                cppWriter.WriteLine($"return reinterpret_cast<{holder}*>(new {cppType}(std::unexpected({ConvertCToCpp(error, "error")})));");
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(bool) {holder}HasValue(const {holder}* self)"))
                cppWriter.WriteLine($"return self != nullptr && reinterpret_cast<const {cppType}*>(self)->has_value();");
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(bool) {holder}TryGetValue(const {holder}* self, {cValue}* out_value)"))
            {
                cppWriter.WriteLine($"if (self == nullptr || !reinterpret_cast<const {cppType}*>(self)->has_value()) return false;");
                cppWriter.WriteLine($"if (out_value != nullptr) *out_value = {ConvertCppToC(value, "reinterpret_cast<const " + cppType + "*>(self)->value()")};");
                cppWriter.WriteLine("return true;");
            }
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(bool) {holder}TryGetError(const {holder}* self, {cError}* out_error)"))
            {
                cppWriter.WriteLine($"if (self == nullptr || reinterpret_cast<const {cppType}*>(self)->has_value()) return false;");
                cppWriter.WriteLine($"if (out_error != nullptr) *out_error = {ConvertCppToC(error, "reinterpret_cast<const " + cppType + "*>(self)->error()")};");
                cppWriter.WriteLine("return true;");
            }
        }

        private bool IsOpaqueValueType(CppType type) => config.IsMapType(type) || config.IsSetType(type) ||
            config.IsVariantType(type) || config.IsExpectedType(type);

        private bool IsContiguousCollection(CppType type) => config.IsSpanType(type) ||
            config.IsVectorType(type) || config.IsArrayType(type);

        private string ConvertCToCpp(CppType type, string expression)
        {
            CppType current = type;
            while (current is CppQualifiedType qualified)
                current = qualified.ElementType;
            while (current is CppTypedef typedef)
                current = typedef.ElementType;
            return current switch
            {
                CppEnum cppEnum => $"static_cast<{cppEnum.FullName}>({expression})",
                CppPointerType => $"reinterpret_cast<{GetOriginalCppTypeName(type)}>({expression})",
                _ => expression
            };
        }

        private string ConvertCppToC(CppType type, string expression) =>
            GetCppReturnExpression(type, config.GetCType(type), expression);

        private void WriteClasses(IEnumerable<CppClass> classes, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            List<CppClass> sourceClasses = classes.Where(IsSupportedClass).ToList();
            foreach (CppClass cppClass in sourceClasses.Where(RequiresOpaqueBridge))
            {
                string typeName = config.GetCTypeName(cppClass);
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
            return config.ResolveTypeAdapter(cppClass, BGCS.Cpp2C.Adapters.CppTypeAdapterUse.Field) == null &&
                (cppClass.ClassKind is CppClassKind.Class or CppClassKind.Struct) &&
                cppClass.TemplateKind != CppTemplateKind.TemplateClass && cppClass.SourceFile != null && cppClass.IsDefinition;
        }

        private static bool RequiresOpaqueBridge(CppClass cppClass)
        {
            return cppClass.ClassKind == CppClassKind.Class || cppClass.BaseTypes.Count > 0 ||
                GetBridgeFunctions(cppClass).Count > 0 || GetBridgeConstructors(cppClass).Count > 0 || cppClass.Destructors.Count > 0 ||
                cppClass.HasVirtualMembers();
        }

        private static List<CppFunction> GetBridgeFunctions(CppClass cppClass)
        {
            return cppClass.Functions.Count > 0
                ? cppClass.Functions.ToList()
                : cppClass.SpecializedTemplate?.Functions.ToList() ?? [];
        }

        private static List<CppFunction> GetBridgeConstructors(CppClass cppClass)
        {
            return cppClass.Constructors.Count > 0
                ? cppClass.Constructors.ToList()
                : cppClass.SpecializedTemplate?.Constructors.ToList() ?? [];
        }

        private string GetSpecializedCType(CppClass cppClass, CppType type)
        {
            if (config.IsUtf8StringType(type))
                return "const char*";
            string? parameterName = type switch
            {
                CppTemplateParameterType parameter => parameter.Name,
                CppUnexposedType unexposed => unexposed.Name,
                _ => null
            };
            if (parameterName != null && cppClass.SpecializedTemplate != null)
            {
                int index = cppClass.SpecializedTemplate.TemplateParameters.ToList()
                    .FindIndex(candidate => candidate is CppTemplateParameterType templateParameter && templateParameter.Name == parameterName);
                if (index >= 0 && index < cppClass.TemplateSpecializedArguments.Count &&
                    cppClass.TemplateSpecializedArguments[index].ArgAsType is CppType argumentType)
                    return config.GetCType(argumentType);
            }
            return type switch
            {
                CppPointerType pointer => GetSpecializedCType(cppClass, pointer.ElementType) + "*",
                CppReferenceType reference => GetSpecializedCType(cppClass, reference.ElementType) + "*",
                CppQualifiedType qualified => GetSpecializedCType(cppClass, qualified.ElementType),
                CppArrayType array => GetSpecializedCType(cppClass, array.ElementType) + "*",
                CppTemplateArgument { ArgAsType: not null } argument => config.GetCType(argument.ArgAsType),
                _ => config.GetCType(type)
            };
        }

        private void WriteDto(CppClass cppClass, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            headerWriter.BeginBlock($"typedef struct");
            if (cppClass.BaseTypes.Any())
            {
                Stack<(CppClass cls, bool close)> stack = new();
                stack.Push((cppClass, false));
                while (stack.Count > 0)
                {
                    var pair = stack.Pop();
                    if (pair.close)
                    {
                        WriteFields(headerWriter, pair.cls.Fields);
                    }
                    else
                    {
                        stack.Push((pair.cls, true));
                        foreach (var baseType in pair.cls.BaseTypes)
                        {
                            if (baseType.Type is CppClass baseClass)
                            {
                                stack.Push((baseClass, false));
                            }
                        }
                    }
                }
            }
            else
            {
                WriteFields(headerWriter, cppClass.Fields);
            }

            headerWriter.EndBlock($"}} {config.GetCTypeName(cppClass)};");
        }

        private void WriteClass(CppClass cppClass, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            var typeName = config.GetCTypeName(cppClass);
            WriteVirtualCallbackProxy(cppClass, typeName, headerWriter, cppWriter);
            WriteConstructors(cppClass, typeName, headerWriter, cppWriter);
            WriteInheritanceCasts(cppClass, typeName, headerWriter, cppWriter);
            CppFunction? callableDestructor = cppClass.Destructors.FirstOrDefault(destructor =>
                (destructor.Visibility is CppVisibility.Public or CppVisibility.Default) && !destructor.Flags.HasFlag(CppFunctionFlags.Deleted));
            bool canDestroy = cppClass.Destructors.Count == 0 || callableDestructor != null;
            if (canDestroy)
            {
                string defaultDestroyName = typeName + "Destroy";
                if (callableDestructor != null && config.IsCallableExcluded(cppClass, callableDestructor, defaultDestroyName))
                    canDestroy = false;
                string destroyName = callableDestructor == null
                    ? defaultDestroyName
                    : config.GetCFunctionName(cppClass, callableDestructor, defaultDestroyName);
                destroyName = GetAvailableFunctionName(destroyName);
                if (canDestroy)
                {
                    definedFunctions.Add(destroyName);
                    headerWriter.WriteLine($"{config.NamePrefix}API(void) {destroyName}({typeName}* self);");
                    using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL(void) {destroyName}({typeName}* self)"))
                    {
                        WriteGuarded(cppWriter, "return;", writer =>
                        {
                            writer.WriteLine($"auto* ptr = reinterpret_cast<{cppClass.FullName}*>(self);");
                            writer.WriteLine("delete ptr;");
                        });
                    }
                }
            }

            IReadOnlyList<CppFunction> functions = GetBridgeFunctions(cppClass);
            for (int j = 0; j < functions.Count; j++)
            {
                var f = functions[j];
                if (f.Visibility != CppVisibility.Public || f.Flags.HasFlag(CppFunctionFlags.Deleted))
                {
                    continue;
                }
                string defaultName = $"{config.GetCTypeName(cppClass)}_{f.Name}";
                if (config.IsCallableExcluded(cppClass, f, defaultName))
                    continue;

                WriteFunctionH(cppClass, f, headerWriter);
                WriteFunctionCpp(cppClass, f, cppWriter);
            }
        }

        private void WriteVirtualCallbackProxy(CppClass cppClass, string typeName, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            if (!config.VirtualCallbackInterfaces.Contains(cppClass.FullName, StringComparer.Ordinal) &&
                !config.VirtualCallbackInterfaces.Contains(cppClass.Name, StringComparer.Ordinal))
                return;
            List<CppFunction> methods = GetBridgeFunctions(cppClass)
                .Where(function => function.Visibility == CppVisibility.Public && function.Flags.HasFlag(CppFunctionFlags.Pure))
                .ToList();
            if (methods.Count == 0)
                throw new InvalidOperationException($"Configured virtual callback interface '{cppClass.FullName}' has no public pure virtual methods.");
            string callbacksType = typeName + "Callbacks";
            List<string> callbackFields = [];
            for (int i = 0; i < methods.Count; i++)
            {
                CppFunction method = methods[i];
                if (method.ReturnType is CppReferenceType || config.IsSpanType(method.ReturnType) || config.IsUniquePtrType(method.ReturnType))
                    throw new NotSupportedException($"Virtual callback return type '{method.ReturnType}' is not supported for '{cppClass.FullName}::{method.Name}'.");
                if (method.Parameters.Any(parameter => config.IsSpanType(parameter.Type) || config.IsUniquePtrType(parameter.Type)))
                    throw new NotSupportedException($"Virtual callback STL parameters require an explicit callback mapping for '{cppClass.FullName}::{method.Name}'.");
                string callbackName = typeName + method.Name + "Callback" + (i == 0 ? string.Empty : i.ToString());
                string parameters = string.Join(", ", new[] { "void* user_data" }.Concat(
                    method.Parameters.Select(parameter => $"{config.GetCType(parameter.Type)} {parameter.Name}")));
                headerWriter.WriteLine($"typedef {config.GetCType(method.ReturnType)} ({config.NamePrefix}CALL *{callbackName})({parameters});");
                callbackFields.Add($"{callbackName} {method.Name}{i};");
            }
            headerWriter.BeginBlock("typedef struct");
            foreach (string callbackField in callbackFields)
                headerWriter.WriteLine(callbackField);
            headerWriter.EndBlock($"}} {callbacksType};");
            headerWriter.WriteLine($"{config.NamePrefix}API({typeName}*) {typeName}CreateProxy({callbacksType} callbacks, void* user_data);");

            string proxyType = typeName + "Proxy";
            using (cppWriter.PushBlock($"class {proxyType} final : public {cppClass.FullName}"))
            {
                cppWriter.WriteLine("public:");
                cppWriter.WriteLine($"{callbacksType} callbacks;");
                cppWriter.WriteLine("void* user_data;");
                cppWriter.WriteLine($"{proxyType}({callbacksType} callbacks, void* user_data) : callbacks(callbacks), user_data(user_data) {{}}");
                for (int i = 0; i < methods.Count; i++)
                {
                    CppFunction method = methods[i];
                    string qualifiers = method.IsConst ? " const override" : " override";
                    using (cppWriter.PushBlock($"{method.ReturnType.GetDisplayName()} {method.Name}({GetCppFunctionSignature(method)}){qualifiers}"))
                    {
                        string field = $"callbacks.{method.Name}{i}";
                        bool returnsVoid = method.ReturnType is CppPrimitiveType { Kind: CppPrimitiveKind.Void };
                        cppWriter.WriteLine($"if ({field} == nullptr) {(returnsVoid ? "return;" : "return {};")}");
                        List<string> arguments = ["user_data"];
                        arguments.AddRange(method.Parameters.Select(parameter => parameter.Type is CppReferenceType ? "&" + parameter.Name : parameter.Name));
                        string invocation = $"{field}({string.Join(", ", arguments)})";
                        cppWriter.WriteLine(returnsVoid ? invocation + ";" : "return " + invocation + ";");
                    }
                }
            }
            cppWriter.WriteLine(";");
            using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({typeName}*) {typeName}CreateProxy({callbacksType} callbacks, void* user_data)"))
            {
                WriteGuarded(cppWriter, "return nullptr;", writer =>
                    writer.WriteLine($"return reinterpret_cast<{typeName}*>(new {proxyType}(callbacks, user_data));"));
            }
        }

        private void WriteInheritanceCasts(CppClass cppClass, string typeName, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            foreach (CppBaseType baseType in cppClass.BaseTypes)
            {
                if (baseType.Type is not CppClass baseClass)
                    continue;
                string baseName = config.GetCTypeName(baseClass);
                string upcastName = typeName + "As" + baseName;
                headerWriter.WriteLine($"{config.NamePrefix}API({baseName}*) {upcastName}({typeName}* self);");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({baseName}*) {upcastName}({typeName}* self)"))
                {
                    cppWriter.WriteLine("if (self == nullptr) return nullptr;");
                    cppWriter.WriteLine($"auto* derived = reinterpret_cast<{cppClass.FullName}*>(self);");
                    cppWriter.WriteLine($"return reinterpret_cast<{baseName}*>(static_cast<{baseClass.FullName}*>(derived));");
                }
                if (!baseClass.HasVirtualMembers())
                    continue;
                string downcastName = typeName + "From" + baseName;
                headerWriter.WriteLine($"{config.NamePrefix}API({typeName}*) {downcastName}({baseName}* self);");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({typeName}*) {downcastName}({baseName}* self)"))
                {
                    cppWriter.WriteLine("if (self == nullptr) return nullptr;");
                    cppWriter.WriteLine($"auto* base_ptr = reinterpret_cast<{baseClass.FullName}*>(self);");
                    cppWriter.WriteLine($"return reinterpret_cast<{typeName}*>(dynamic_cast<{cppClass.FullName}*>(base_ptr));");
                }
            }
        }

        private void WriteFreeFunctions(IEnumerable<CppFunction> functions, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            foreach (CppFunction function in functions)
            {
                if (function.IsExternC || function.Flags.HasFlag(CppFunctionFlags.FunctionTemplate) || function.TemplateParameters.Count > 0 ||
                    function.Visibility is not (CppVisibility.Public or CppVisibility.Default))
                    continue;
                string baseName = config.GetCFunctionName(function);
                if (config.IsCallableExcluded(null, function, baseName))
                    continue;
                string name = baseName;
                int suffix = 1;
                while (!definedFunctions.Add(name))
                    name = baseName + suffix++;
                bool returnsCollection = IsContiguousCollection(function.ReturnType);
                bool returnsOptional = config.IsOptionalType(function.ReturnType);
                string cReturnType = returnsOptional ? "bool" : config.GetCType(function.ReturnType);
                string cSignature = GetCParameterSignature(function.Parameters);
                if (returnsCollection)
                    cSignature = cSignature == "void" ? "size_t* out_count" : cSignature + ", size_t* out_count";
                if (returnsOptional)
                {
                    if (!config.TryGetTemplateElementType(function.ReturnType, out CppType? optionalElement))
                        throw new NotSupportedException($"Unable to resolve optional return '{function.ReturnType}'.");
                    string output = config.GetCType(function.ReturnType) + "* out_value";
                    cSignature = cSignature == "void" ? output : cSignature + ", " + output;
                }
                string arguments = GetCppFunctionSignatureTypeless(function);
                headerWriter.WriteLine($"{config.NamePrefix}API({cReturnType}) {name}({cSignature});");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({cReturnType}) {name}({cSignature})"))
                {
                    bool returnsVoid = function.ReturnType is CppPrimitiveType { Kind: CppPrimitiveKind.Void };
                    string failureStatement = returnsVoid ? "return;" : cReturnType.EndsWith('*') ? "return nullptr;" : "return {};";
                    WriteGuarded(cppWriter, failureStatement, writer =>
                    {
                        string qualifiedName = string.IsNullOrEmpty(function.FullParentName)
                            ? function.Name
                            : function.FullParentName + "::" + function.Name;
                        string invocation = $"{qualifiedName}({arguments})";
                        if (returnsVoid)
                            writer.WriteLine(invocation + ";");
                        else if (returnsCollection)
                        {
                            if (config.IsVectorType(function.ReturnType) || config.IsArrayType(function.ReturnType))
                            {
                                writer.WriteLine($"thread_local {GetCppValueTypeName(function.ReturnType)} return_value;");
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
                            config.TryGetTemplateElementType(function.ReturnType, out CppType? optionalElement);
                            if (config.IsBlittableBridgeType(optionalElement!))
                                writer.WriteLine("if (out_value != nullptr) *out_value = *optional_result;");
                            else
                                writer.WriteLine($"if (out_value != nullptr) *out_value = reinterpret_cast<{config.GetCType(optionalElement!)}*>(new {GetCppValueTypeName(optionalElement!)}(*optional_result));");
                            writer.WriteLine("return true;");
                        }
                        else if (IsOpaqueValueType(function.ReturnType))
                        {
                            string holder = config.GetOpaqueValueHolderName(function.ReturnType);
                            writer.WriteLine($"return reinterpret_cast<{holder}*>(new {GetCppValueTypeName(function.ReturnType)}({invocation}));");
                        }
                        else if (config.IsSharedPtrType(function.ReturnType))
                        {
                            writer.WriteLine($"return reinterpret_cast<{config.GetSharedPtrHolderName(function.ReturnType)}*>(new {GetCppValueTypeName(function.ReturnType)}({invocation}));");
                        }
                        else if (config.IsUniquePtrType(function.ReturnType))
                        {
                            writer.WriteLine($"return {invocation}.release();");
                        }
                        else if (config.IsPathType(function.ReturnType))
                        {
                            writer.WriteLine("thread_local std::string return_value;");
                            writer.WriteLine($"auto path_value = {invocation};");
                            writer.WriteLine("auto utf8_value = path_value.u8string();");
                            writer.WriteLine("return_value.assign(reinterpret_cast<const char*>(utf8_value.data()), utf8_value.size());");
                            writer.WriteLine("return return_value.c_str();");
                        }
                        else if (config.IsUtf8StringType(function.ReturnType))
                        {
                            writer.WriteLine($"thread_local {GetUtf8StringValueTypeName(function.ReturnType)} return_value;");
                            writer.WriteLine($"return_value = {invocation};");
                            writer.WriteLine("return return_value.c_str();");
                        }
                        else if (config.IsChronoDurationType(function.ReturnType))
                            writer.WriteLine($"return static_cast<int64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>({invocation}).count());");
                        else if (config.IsChronoTimePointType(function.ReturnType))
                            writer.WriteLine($"return static_cast<int64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(({invocation}).time_since_epoch()).count());");
                        else if (function.ReturnType is CppReferenceType)
                            writer.WriteLine($"return {GetCppReferenceReturnExpression(function.ReturnType, cReturnType, invocation)};");
                        else
                            writer.WriteLine($"return {GetCppReturnExpression(function.ReturnType, cReturnType, invocation)};");
                    });
                }
            }
        }

        private string GetCParameterSignature(IEnumerable<CppParameter> parameters)
        {
            string signature = string.Join(", ", parameters.SelectMany(parameter => GetCParameterDeclarations(null, parameter)));
            return string.IsNullOrEmpty(signature) ? "void" : signature;
        }

        private void WriteGuarded(ICodeWriter writer, string failureStatement, Action<ICodeWriter> body)
        {
            using (writer.PushBlock("try"))
            {
                writer.WriteLine($"{config.ErrorSymbolPrefix}ClearLastError();");
                body(writer);
            }
            using (writer.PushBlock("catch (const std::exception& exception)"))
            {
                writer.WriteLine($"{config.ErrorSymbolPrefix}last_error = exception.what();");
                writer.WriteLine(failureStatement);
            }
            using (writer.PushBlock("catch (...)"))
            {
                writer.WriteLine($"{config.ErrorSymbolPrefix}last_error = \"Unknown C++ exception\";");
                writer.WriteLine(failureStatement);
            }
        }

        private void WriteConstructors(CppClass cppClass, string typeName, ICodeWriter headerWriter, ICodeWriter cppWriter)
        {
            if (GetBridgeFunctions(cppClass).Any(function => function.Flags.HasFlag(CppFunctionFlags.Pure)))
            {
                return;
            }

            IReadOnlyList<CppFunction> availableConstructors = GetBridgeConstructors(cppClass);
            List<CppFunction?> constructors = availableConstructors.Count == 0
                ? [null]
                : availableConstructors
                    .Where(constructor => (constructor.Visibility is CppVisibility.Public or CppVisibility.Default) && !constructor.Flags.HasFlag(CppFunctionFlags.Deleted))
                    .Cast<CppFunction?>()
                    .ToList();
            for (int i = 0; i < constructors.Count; i++)
            {
                CppFunction? constructor = constructors[i];
                string defaultName = typeName + "Create" + (i == 0 ? string.Empty : i.ToString());
                if (constructor != null && config.IsCallableExcluded(cppClass, constructor, defaultName))
                    continue;
                string name = constructor == null
                    ? defaultName
                    : config.GetCFunctionName(cppClass, constructor, defaultName);
                name = GetAvailableFunctionName(name);
                definedFunctions.Add(name);
                string cSignature = constructor == null ? "void" : GetCParameterSignature(cppClass, constructor.Parameters);
                string arguments = constructor == null ? string.Empty : GetCppFunctionSignatureTypeless(constructor);
                headerWriter.WriteLine($"{config.NamePrefix}API({typeName}*) {name}({cSignature});");
                using (cppWriter.PushBlock($"{config.NamePrefix}API_INTERNAL({typeName}*) {name}({cSignature})"))
                {
                    WriteGuarded(cppWriter, "return nullptr;", writer =>
                        writer.WriteLine($"return reinterpret_cast<{typeName}*>(new {cppClass.FullName}({arguments}));"));
                }
            }
        }

        private string GetCParameterSignature(CppClass cppClass, IEnumerable<CppParameter> parameters)
        {
            string signature = string.Join(", ", parameters.SelectMany(parameter => GetCParameterDeclarations(cppClass, parameter)));
            return string.IsNullOrEmpty(signature) ? "void" : signature;
        }

        private IEnumerable<string> GetCParameterDeclarations(CppClass? cppClass, CppParameter parameter)
        {
            string type = cppClass == null ? config.GetCType(parameter.Type) : GetSpecializedCType(cppClass, parameter.Type);
            yield return $"{type} {parameter.Name}";
            if (IsContiguousCollection(parameter.Type))
                yield return $"size_t {parameter.Name}_count";
            else if (config.IsOptionalType(parameter.Type))
                yield return $"bool {parameter.Name}_has_value";
        }

        private void WriteFields(ICodeWriter writer, IEnumerable<CppField> fields)
        {
            foreach (var field in fields)
            {
                if ((field.StorageQualifier & CppStorageQualifier.Static) != 0)
                {
                    continue;
                }
                writer.WriteLine($"{config.GetCType(field.Type)} {field.Name};");
            }
        }

        private string GetCFunctionSignature(CppClass c, CppFunction f)
        {
            StringBuilder sb = new();
            bool isStatic = (f.StorageQualifier & CppStorageQualifier.Static) != 0;
            if (!isStatic)
            {
                sb.Append($"{config.GetCType(c)}* self");
            }

            foreach (CppParameter parameter in f.Parameters)
            {
                foreach (string declaration in GetCParameterDeclarations(c, parameter))
                {
                    if (sb.Length > 0)
                        sb.Append(", ");
                    sb.Append(declaration);
                }
            }

            if (IsContiguousCollection(f.ReturnType))
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append("size_t* out_count");
            }
            if (config.IsOptionalType(f.ReturnType))
            {
                if (!config.TryGetTemplateElementType(f.ReturnType, out CppType? optionalElement))
                    throw new NotSupportedException($"Unable to resolve optional return '{f.ReturnType}'.");
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append($"{config.GetCType(f.ReturnType)}* out_value");
            }
            return sb.Length == 0 ? "void" : sb.ToString();
        }

        private static string GetCppFunctionSignature(CppFunction f)
        {
            StringBuilder sb = new();

            for (int i = 0; i < f.Parameters.Count; i++)
            {
                var param = f.Parameters[i];
                sb.Append($"{param.Type} {param.Name}");
                if (i < f.Parameters.Count - 1)
                {
                    sb.Append(", ");
                }
            }

            return sb.ToString();
        }

        private string GetCppValueTypeName(CppType type) => config.GetCppValueTypeSpelling(type);

        private string GetUtf8StringValueTypeName(CppType type) => config.GetCppValueTypeSpelling(type);

        private static string GetCppReferenceReturnExpression(CppType type, string cReturnType, string invocation)
        {
            CppType current = type is CppReferenceType reference ? reference.ElementType : type;
            while (current is CppQualifiedType qualified)
                current = qualified.ElementType;
            return current is CppPrimitiveType
                ? $"&({invocation})"
                : $"reinterpret_cast<{cReturnType}>(&({invocation}))";
        }

        private static string GetCppReturnExpression(CppType type, string cReturnType, string invocation)
        {
            if (Cpp2CGeneratorConfig.IsFunctionPointerType(type))
                return $"reinterpret_cast<{cReturnType}>({invocation})";
            CppType current = type;
            while (current is CppQualifiedType qualified)
                current = qualified.ElementType;
            while (current is CppTypedef typedef)
                current = typedef.ElementType;
            if (current is CppEnum)
                return $"static_cast<{cReturnType}>({invocation})";
            if (current is CppPointerType)
                return $"reinterpret_cast<{cReturnType}>({invocation})";
            return invocation;
        }

        private string GetOriginalCppTypeName(CppType type)
        {
            return type switch
            {
                CppPointerType pointer => GetOriginalCppTypeName(pointer.ElementType) + "*",
                CppReferenceType reference => GetOriginalCppTypeName(reference.ElementType) + "&",
                CppQualifiedType qualified => (qualified.Qualifier == CppTypeQualifier.Const ? "const " : "volatile ") + GetOriginalCppTypeName(qualified.ElementType),
                CppClass cppClass => cppClass.FullName,
                CppEnum cppEnum => cppEnum.FullName,
                CppTypedef typedef when !string.IsNullOrEmpty(typedef.FullParentName) => typedef.FullParentName + "::" + typedef.Name,
                CppUnexposedType unexposed when cppQualifiedTypeNames.TryGetValue(unexposed.Name, out string? fullName) => fullName,
                _ => type.GetDisplayName()
            };
        }

        private string GetCppArgumentExpression(CppParameter parameter)
        {
            CppType type = parameter.Type;
            CppType current = type;
            while (current is CppQualifiedType qualified)
                current = qualified.ElementType;
            if (current is CppTypedef typedef && Cpp2CGeneratorConfig.IsFunctionPointerType(typedef))
                return $"reinterpret_cast<{GetCppValueTypeName(typedef)}>({parameter.Name})";
            while (current is CppTypedef nested)
                current = nested.ElementType;
            if (current is CppEnum cppEnum)
                return $"static_cast<{cppEnum.FullName}>({parameter.Name})";
            if (current is CppReferenceType reference)
            {
                CppType element = reference.ElementType;
                while (element is CppQualifiedType qualifiedElement)
                    element = qualifiedElement.ElementType;
                if (element is CppPrimitiveType)
                    return "*" + parameter.Name;
                return $"*reinterpret_cast<{GetOriginalCppTypeName(reference.ElementType)}*>({parameter.Name})";
            }
            if (current is CppPointerType pointer && pointer.ElementType is not CppPrimitiveType { Kind: CppPrimitiveKind.Void })
                return $"reinterpret_cast<{GetOriginalCppTypeName(type)}>({parameter.Name})";
            return parameter.Name;
        }

        private string GetCppFunctionSignatureTypeless(CppFunction f)
        {
            StringBuilder sb = new();

            for (int i = 0; i < f.Parameters.Count; i++)
            {
                var param = f.Parameters[i];
                if (config.IsOptionalType(param.Type))
                {
                    if (!config.TryGetTemplateElementType(param.Type, out CppType? elementType))
                        throw new NotSupportedException($"Unable to resolve optional parameter '{param.Type}'.");
                    string optionalType = config.OptionalTypes[0] + "<" + GetCppValueTypeName(elementType!) + ">";
                    string value = config.IsBlittableBridgeType(elementType!)
                        ? param.Name
                        : $"*reinterpret_cast<{GetCppValueTypeName(elementType!)}*>({param.Name})";
                    sb.Append($"{param.Name}_has_value ? {optionalType}({value}) : std::nullopt");
                }
                else if (config.IsArrayType(param.Type))
                {
                    if (!config.TryGetTemplateElementType(param.Type, out CppType? elementType))
                        throw new NotSupportedException($"Unable to resolve array parameter '{param.Type}'.");
                    long elementCount = config.GetArrayElementCount(param.Type);
                    string elementName = GetCppValueTypeName(elementType!);
                    string arrayType = GetCppValueTypeName(param.Type);
                    sb.Append($"([&]() {{ if ({param.Name}_count != {elementCount} || ({elementCount} != 0 && {param.Name} == nullptr)) throw std::invalid_argument(\"Invalid fixed array extent\"); {arrayType} converted{{}}; std::copy_n(reinterpret_cast<const {elementName}*>({param.Name}), {elementCount}, converted.begin()); return converted; }}())");
                }
                else if (config.IsVectorType(param.Type))
                {
                    if (!config.TryGetTemplateElementType(param.Type, out CppType? elementType))
                        throw new NotSupportedException($"Unable to resolve vector parameter '{param.Type}'.");
                    string elementName = GetCppValueTypeName(elementType!);
                    string vectorType = config.VectorTypes[0] + "<" + elementName + ">";
                    sb.Append($"{vectorType}(reinterpret_cast<{elementName}*>({param.Name}), reinterpret_cast<{elementName}*>({param.Name}) + {param.Name}_count)");
                }
                else if (config.IsSpanType(param.Type))
                {
                    if (!config.TryGetTemplateElementType(param.Type, out CppType? elementType))
                        throw new NotSupportedException($"Unable to resolve span parameter '{param.Type}'.");
                    string spanType = config.SpanTypes[0] + "<" + GetCppValueTypeName(elementType!) + ">";
                    sb.Append($"{spanType}(reinterpret_cast<{GetCppValueTypeName(elementType!)}*>({param.Name}), {param.Name}_count)");
                }
                else if (config.IsSharedPtrType(param.Type))
                {
                    if (!config.TryGetTemplateElementType(param.Type, out CppType? elementType))
                        throw new NotSupportedException($"Unable to resolve shared_ptr parameter '{param.Type}'.");
                    sb.Append($"*reinterpret_cast<{GetCppValueTypeName(param.Type)}*>({param.Name})");
                }
                else if (config.IsUniquePtrType(param.Type))
                {
                    if (!config.TryGetTemplateElementType(param.Type, out CppType? elementType))
                        throw new NotSupportedException($"Unable to resolve unique_ptr parameter '{param.Type}'.");
                    sb.Append($"{GetCppValueTypeName(param.Type)}(reinterpret_cast<{GetCppValueTypeName(elementType!)}*>({param.Name}))");
                }
                else if (IsOpaqueValueType(param.Type))
                {
                    sb.Append($"*reinterpret_cast<{GetCppValueTypeName(param.Type)}*>({param.Name})");
                }
                else if (config.IsPathType(param.Type))
                {
                    sb.Append($"({param.Name} == nullptr ? std::filesystem::path() : std::filesystem::path(std::u8string(reinterpret_cast<const char8_t*>({param.Name}))))");
                }
                else if (config.IsChronoDurationType(param.Type))
                {
                    string typeName = GetCppValueTypeName(param.Type);
                    sb.Append($"std::chrono::duration_cast<{typeName}>(std::chrono::nanoseconds({param.Name}))");
                }
                else if (config.IsChronoTimePointType(param.Type))
                {
                    string typeName = GetCppValueTypeName(param.Type);
                    sb.Append($"{typeName}(std::chrono::duration_cast<{typeName}::duration>(std::chrono::nanoseconds({param.Name})))");
                }
                else if (config.IsUtf8StringType(param.Type))
                {
                    string typeName = GetUtf8StringValueTypeName(param.Type);
                    sb.Append($"{typeName}({param.Name} == nullptr ? \"\" : {param.Name})");
                }
                else
                {
                    sb.Append(GetCppArgumentExpression(param));
                }
                if (i < f.Parameters.Count - 1)
                {
                    sb.Append(", ");
                }
            }

            return sb.ToString();
        }

        private string GetUniqueCFunctionName(CppClass c, CppFunction f)
        {
            string defaultName = $"{config.GetCTypeName(c)}_{f.Name}";
            string cName = config.GetCFunctionName(c, f, defaultName);
            return GetAvailableFunctionName(cName);
        }

        private string GetAvailableFunctionName(string requestedName)
        {
            int suffix = 1;
            string currentName = requestedName;
            while (definedFunctions.Contains(currentName))
                currentName = requestedName + suffix++;
            return currentName;
        }

        private void WriteFunctionH(CppClass c, CppFunction f, ICodeWriter writer)
        {
            string name = GetUniqueCFunctionName(c, f);
            string cSignature = GetCFunctionSignature(c, f);
            string cReturnType = config.IsOptionalType(f.ReturnType) ? "bool" : GetSpecializedCType(c, f.ReturnType);
            definedFunctions.Add(name);
            mapping.Add((c, f), name);

            definedCFunctions.Add(new(name, f.Name, f.Comment?.ToString()));

            writer.WriteLine($"{config.NamePrefix}API({cReturnType}) {name}({cSignature});");
        }

        private void WriteFunctionCpp(CppClass c, CppFunction f, ICodeWriter writer)
        {
            var name = mapping[(c, f)];
            string cSignature = GetCFunctionSignature(c, f);
            string signature = GetCppFunctionSignatureTypeless(f);
            bool returnsCollection = IsContiguousCollection(f.ReturnType);
            string cReturnType = config.IsOptionalType(f.ReturnType) ? "bool" : GetSpecializedCType(c, f.ReturnType);
            bool isStatic = (f.StorageQualifier & CppStorageQualifier.Static) != 0;

            using (writer.PushBlock($"{config.NamePrefix}API_INTERNAL({cReturnType}) {name}({cSignature})"))
            {
                bool returnsVoid = f.ReturnType is CppPrimitiveType { Kind: CppPrimitiveKind.Void };
                string failureStatement = returnsVoid ? "return;" : cReturnType.EndsWith('*') ? "return nullptr;" : "return {};";
                WriteGuarded(writer, failureStatement, guardedWriter =>
                {
                    if (!isStatic)
                    {
                        guardedWriter.WriteLine($"auto* ptr = reinterpret_cast<{c.FullName}*>(self);");
                    }
                    string invocation = isStatic
                        ? $"{c.FullName}::{f.Name}({signature})"
                        : $"ptr->{f.Name}({signature})";
                    if (returnsVoid)
                    {
                        guardedWriter.WriteLine($"{invocation};");
                    }
                    else if (returnsCollection)
                    {
                        if (config.IsVectorType(f.ReturnType) || config.IsArrayType(f.ReturnType))
                        {
                            guardedWriter.WriteLine($"thread_local {GetCppValueTypeName(f.ReturnType)} return_value;");
                            guardedWriter.WriteLine($"return_value = {invocation};");
                        }
                        else
                        {
                            guardedWriter.WriteLine($"auto return_value = {invocation};");
                        }
                        guardedWriter.WriteLine("if (out_count != nullptr) *out_count = return_value.size();");
                        guardedWriter.WriteLine("return return_value.data();");
                    }
                    else if (config.IsOptionalType(f.ReturnType))
                    {
                        guardedWriter.WriteLine($"auto optional_result = {invocation};");
                        guardedWriter.WriteLine("if (!optional_result.has_value()) return false;");
                        config.TryGetTemplateElementType(f.ReturnType, out CppType? optionalElement);
                        if (config.IsBlittableBridgeType(optionalElement!))
                            guardedWriter.WriteLine("if (out_value != nullptr) *out_value = *optional_result;");
                        else
                            guardedWriter.WriteLine($"if (out_value != nullptr) *out_value = reinterpret_cast<{config.GetCType(optionalElement!)}*>(new {GetCppValueTypeName(optionalElement!)}(*optional_result));");
                        guardedWriter.WriteLine("return true;");
                    }
                    else if (IsOpaqueValueType(f.ReturnType))
                    {
                        string holder = config.GetOpaqueValueHolderName(f.ReturnType);
                        guardedWriter.WriteLine($"return reinterpret_cast<{holder}*>(new {GetCppValueTypeName(f.ReturnType)}({invocation}));");
                    }
                    else if (config.IsSharedPtrType(f.ReturnType))
                    {
                        guardedWriter.WriteLine($"return reinterpret_cast<{config.GetSharedPtrHolderName(f.ReturnType)}*>(new {GetCppValueTypeName(f.ReturnType)}({invocation}));");
                    }
                    else if (config.IsUniquePtrType(f.ReturnType))
                    {
                        guardedWriter.WriteLine($"return {invocation}.release();");
                    }
                    else if (config.IsPathType(f.ReturnType))
                    {
                        guardedWriter.WriteLine("thread_local std::string return_value;");
                        guardedWriter.WriteLine($"auto path_value = {invocation};");
                        guardedWriter.WriteLine("auto utf8_value = path_value.u8string();");
                        guardedWriter.WriteLine("return_value.assign(reinterpret_cast<const char*>(utf8_value.data()), utf8_value.size());");
                        guardedWriter.WriteLine("return return_value.c_str();");
                    }
                    else if (config.IsUtf8StringType(f.ReturnType))
                    {
                        guardedWriter.WriteLine($"thread_local {GetUtf8StringValueTypeName(f.ReturnType)} return_value;");
                        guardedWriter.WriteLine($"return_value = {invocation};");
                        guardedWriter.WriteLine("return return_value.c_str();");
                    }
                    else if (config.IsChronoDurationType(f.ReturnType))
                        guardedWriter.WriteLine($"return static_cast<int64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>({invocation}).count());");
                    else if (config.IsChronoTimePointType(f.ReturnType))
                        guardedWriter.WriteLine($"return static_cast<int64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(({invocation}).time_since_epoch()).count());");
                    else if (f.ReturnType is CppReferenceType)
                    {
                        guardedWriter.WriteLine($"return {GetCppReferenceReturnExpression(f.ReturnType, cReturnType, invocation)};");
                    }
                    else
                    {
                        guardedWriter.WriteLine($"return {GetCppReturnExpression(f.ReturnType, cReturnType, invocation)};");
                    }
                });
            }
        }
    }
