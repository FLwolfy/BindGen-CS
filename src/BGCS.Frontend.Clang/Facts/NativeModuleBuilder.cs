using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Frontend.Clang.Model;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Model.Interfaces;
using BGCS.Frontend.Clang.Model.Metadata;
using BGCS.Frontend.Clang.Model.Types;
using BGCS.Frontend.Clang.Model.Templates;
using BGCS.Frontend.Clang.Parsing;
using ClangSharp.Interop;
using BGCS.Frontend.Clang.Utilities;

namespace BGCS.Frontend.Clang.Facts;

internal sealed class NativeModuleBuilder
{
    private readonly FrontendRequest m_request;
    private readonly SourcePathMapper m_sourcePaths;
    private readonly Dictionary<string, NativeType> m_types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeDeclaration> m_declarations = new(StringComparer.Ordinal);
    private readonly Dictionary<StableId, CXCursor> m_declarationCursors = [];
    private readonly HashSet<string> m_visitingTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_visitingDeclarations = new(StringComparer.Ordinal);
    private CppCompilation? m_compilation;

    internal NativeModuleBuilder(
        FrontendRequest request,
        CppCompilation compilation
    ) {
        m_request = request;
        m_sourcePaths = new(request.sourcePathMappings, request.pathBase, IncludeDependencyCollector.GetSourceFiles(compilation));
    }

    internal NativeModule Build(CppCompilation compilation)
    {
        m_compilation = compilation;
        foreach (ICppDeclaration declaration in compilation.children)
            AddDeclaration((CppElement)declaration, null);
        foreach (CppMacro macro in compilation.macros)
            AddDeclaration(macro, null);
        ImmutableArray<BindingDiagnostic> diagnostics = compilation.diagnostics.messages
            .Select(message => new BindingDiagnostic(
                message.type.ToString() switch { "Error" => BindingDiagnosticSeverity.Error, "Warning" => BindingDiagnosticSeverity.Warning, _ => BindingDiagnosticSeverity.Information },
                m_sourcePaths.MapDiagnostic(message.text),
                "BGCS-FRONTEND-CLANG")
            { location = string.IsNullOrEmpty(message.location.file) ? null : new SourceLocation(m_sourcePaths.MapPath(message.location.file), message.location.line, message.location.column) })
            .ToImmutableArray();
        return new NativeModule(
            m_request.target.targetId.value,
            m_request.target.triple,
            compilation.pointerSize,
            m_types.Values.OrderBy(type => type.id.value, StringComparer.Ordinal).ToImmutableArray(),
            m_declarations.Values.OrderBy(declaration => declaration.id.value, StringComparer.Ordinal).ToImmutableArray(),
            diagnostics)
        { language = m_request.language, byteOrder = m_request.target.byteOrder };
    }

    internal BGCS.Frontend.Clang.Advanced.ClangFactContributionContext CreateContributionContext(CXTranslationUnit translationUnit)
        => new(translationUnit, m_request, m_declarationCursors, CursorLocation);

    private StableId AddType(CppType type)
    {
        // A declaration need not occur in a signature or field. Capture its
        // compiler spelling here as well: bare C record tags and anonymous
        // typedef records occupy different namespaces and cannot be invented
        // from the model's display name.
        if ((type is CppEnum || m_request.language == SourceLanguage.C && type is CppClass)
            && type.cursor.kind != 0 && !m_compilation!.storageFacts.ContainsKey(type))
            CaptureCanonicalStorage(type, type.cursor.Type.CanonicalType.UnqualifiedType, new(ReferenceEqualityComparer.Instance));
        bool hasStorage = m_compilation!.storageFacts.TryGetValue(type, out NativeStorageFacts storage);
        // Display names deliberately abbreviate named pointees/aliases. Their
        // compound shapes can share a spelling such as value_type* across
        // different concrete owners, while the compiler's canonical storage
        // spelling retains the actual pointee and every qualification level.
        string identity = Identity("type", type, m_sourcePaths.MapCompilerProvenance(type.fullName) + "|" + type.typeKind + "|" + type.sizeOf
            + "|" + m_sourcePaths.MapCompilerProvenance(hasStorage ? storage.TypeSpelling ?? string.Empty : string.Empty));
        StableId id = new(identity);
        if (m_types.ContainsKey(identity) || !m_visitingTypes.Add(identity))
            return id;
        NativeTypeCategory category = type switch
        {
            CppPrimitiveType => NativeTypeCategory.Primitive,
            CppPointerType => NativeTypeCategory.Pointer,
            CppReferenceType => NativeTypeCategory.Reference,
            CppQualifiedType => NativeTypeCategory.Qualified,
            CppArrayType => NativeTypeCategory.Array,
            CppClass => NativeTypeCategory.Record,
            CppEnum => NativeTypeCategory.Enumeration,
            CppTypedef => NativeTypeCategory.Alias,
            CppFunctionTypeBase => NativeTypeCategory.Function,
            CppUnexposedType { isBlockPointer: true } => NativeTypeCategory.Block,
            CppTemplateArgument or CppTemplateParameterType or CppTemplateParameterNonType or CppTemplateParameterTemplate => NativeTypeCategory.Template,
            _ => NativeTypeCategory.Unexposed
        };
        NativeType fact = new(id, category, type.fullName, type.sizeOf >= 0 ? type.sizeOf : null,
            type is CppClass record && record.alignOf > 0 ? record.alignOf : null);
        if (type is CppTypedef) fact = fact with { spelling = QualifiedMemberName(type, type.fullName) };
        if (hasStorage)
            fact = fact with
            {
                size = storage.Size,
                alignment = storage.Alignment,
                spelling = type is CppClass or CppEnum && !string.IsNullOrWhiteSpace(storage.TypeSpelling) ? storage.TypeSpelling : fact.spelling
            };
        if (type is CppPrimitiveType { kind: CppPrimitiveKind.Void }) fact = fact with { size = 0 };
        if (type is CppClass { isCompleteDefinition: true } complete && complete.alignOf > 0)
            fact = fact with { alignment = complete.alignOf, size = complete.sizeOf };
        if (type is CppClass { isDefinition: false })
            fact = fact with { size = null };
        fact = fact with { spelling = m_sourcePaths.MapCompilerProvenance(fact.spelling) };
        if (type is CppReferenceType reference)
            fact = fact with { isRValueReference = reference.isRValue };
        if (type is CppTypeWithElementType element)
            fact = fact with { elementType = AddType(element.elementType) };
        if (type is CppTypedef alias)
        {
            CppType canonical = alias.GetCanonicalType();
            if (alias.cursor.kind != 0) CaptureCanonicalStorage(canonical, alias.cursor.Type.CanonicalType, new(ReferenceEqualityComparer.Instance));
            fact = fact with { elementType = AddType(alias.elementType), canonicalType = AddType(canonical) };
        }
        if (type is CppPrimitiveType primitive)
        {
            fact = fact with { primitiveKind = primitive.kind.ToString() };
            if (m_compilation!.primitiveFacts.TryGetValue(primitive, out PrimitiveTypeFacts facts))
                fact = fact with { alignment = facts.Alignment, isSigned = facts.IsSigned };
        }
        if (type is CppQualifiedType qualified)
            fact = fact with { qualifier = qualified.qualifier.ToString() };
        if (type is CppArrayType array)
            fact = fact with { arrayLength = m_compilation.storageFacts.TryGetValue(array, out NativeStorageFacts arrayStorage) ? arrayStorage.ArrayLength : array.size > 0 ? array.size : null };
        if (type is CppEnum enumeration)
            fact = fact with { elementType = AddType(enumeration.integerType) };
        CppFunctionTypeBase? callable = type as CppFunctionTypeBase ?? (type as CppUnexposedType)?.blockSignature;
        if (callable is not null)
        {
            fact = fact with
            {
                returnType = AddType(callable.returnType),
                parameterTypes = callable.parameters.Select(parameter => AddType(parameter.type)).ToImmutableArray(),
                callingConvention = callable.callingConvention.ToString(),
                isVariadic = callable.isVariadic,
                hasPrototype = callable.hasPrototype
            };
        }
        if (type is ICppDeclaration declaration)
            fact = fact with { declaration = AddDeclaration((CppElement)declaration, null) };
        m_types.Add(identity, fact);
        return id;
    }

    private void CaptureCanonicalStorage(
        CppType shape,
        CXType native,
        HashSet<CppType> visited
    ) {
        if (native.kind == CXTypeKind.CXType_Invalid || !visited.Add(shape)) return;
        if (!m_compilation!.storageFacts.ContainsKey(shape))
        {
            long size = native.SizeOf, alignment = native.AlignOf;
            m_compilation.storageFacts[shape] = new(size >= 0 ? size : null, alignment > 0 ? checked((int)alignment) : null,
                native.kind == CXTypeKind.CXType_ConstantArray ? native.ArraySize : null, CXUtil.GetTypeSpelling(native.CanonicalType));
        }
        if (shape is CppQualifiedType qualified) CaptureCanonicalStorage(qualified.elementType, native.UnqualifiedType, visited);
        else if (shape is CppPointerType pointer && native.kind == CXTypeKind.CXType_Pointer)
            CaptureCanonicalStorage(pointer.elementType, native.PointeeType.CanonicalType, visited);
        else if (shape is CppReferenceType reference && native.kind is CXTypeKind.CXType_LValueReference or CXTypeKind.CXType_RValueReference)
            CaptureCanonicalStorage(reference.elementType, native.PointeeType.CanonicalType, visited);
        else if (shape is CppArrayType array && native.kind is CXTypeKind.CXType_ConstantArray or CXTypeKind.CXType_IncompleteArray or CXTypeKind.CXType_VariableArray or CXTypeKind.CXType_DependentSizedArray)
            CaptureCanonicalStorage(array.elementType, native.ArrayElementType.CanonicalType, visited);
    }

    private StableId AddDeclaration(
        CppElement element,
        StableId? parent
    ) {
        CppElement declaration = element;
        string name = declaration is ICppMember member ? member.name : string.Empty;
        string fallback = name + "|" + element.GetType().Name;
        if (element is CppType declarationType) fallback += "|" + declarationType.fullName;
        if (element is CppParameter parameterIdentity)
        {
            int index = element.parent switch
            {
                CppFunction ownerFunction => ownerFunction.parameters.IndexOf(parameterIdentity),
                CppFunctionTypeBase callback => callback.parameters.IndexOf(parameterIdentity),
                _ => -1
            };
            fallback += "|" + parent?.value + "|" + index.ToString(CultureInfo.InvariantCulture);
        }
        string identity = Identity("declaration", element, fallback);
        StableId id = new(identity);
        if (m_declarations.ContainsKey(identity) || !m_visitingDeclarations.Add(identity))
            return id;
        if (parent is null && element.parent is CppElement parentElement && parentElement is ICppDeclaration)
            parent = AddDeclaration(parentElement, null);
        CppType? declaredType = declaration switch
        {
            CppType type => type,
            CppField field => field.type,
            CppParameter parameter => parameter.type,
            _ => null
        };
        string qualifiedName = string.IsNullOrEmpty(element.fullParentName) ? name : element.fullParentName + "::" + name;
        qualifiedName = m_sourcePaths.MapCompilerProvenance(QualifiedMemberName(element, qualifiedName));
        NativeDeclaration fact = new(id, element.GetType().Name.Replace("Cpp", string.Empty, StringComparison.Ordinal), name,
            qualifiedName, declaredType is null ? null : AddType(declaredType), GetLocation(element))
        {
            parent = parent,
            documentation = (declaration as ICppDeclaration)?.comment?.ToString(),
            documentationContent = CommentFactReader.Read((declaration as ICppDeclaration)?.comment),
            sourceTypeSpelling = declaration is CppParameter writtenParameter ? m_sourcePaths.MapCompilerProvenance(writtenParameter.sourceTypeSpelling) : null,
            sourceType = declaration is CppParameter sourceParameter ? AddType(sourceParameter.sourceType) : null,
            visibility = declaration is ICppMemberWithVisibility visible ? visible.visibility.ToString() : null,
            isSystemDeclaration = element.cursor.kind != 0 && element.cursor.Location.IsInSystemHeader,
            children = element is ICppContainer container
                ? DeclarationChildren(container).Select(child => AddDeclaration((CppElement)child, id)).ToImmutableArray()
                : []
        };
        if (declaration is CppFunction function)
        {
            fact = fact with
            {
                returnType = AddType(function.returnType),
                isStatic = function.isStatic,
                isInline = function.flags.HasFlag(CppFunctionFlags.Inline) || function.cursor.Definition.IsFunctionInlined,
                // libclang's IsDefinition follows the materialized body and is false
                // when SkipFunctionBodies discarded it. Clang's declaration fact
                // also recognizes skipped and explicitly defaulted definitions.
                isDefinition = function.cursor.IsDefinition || function.cursor.IsThisDeclarationADefinition
                    || function.cursor.Definition.IsDefinition || function.cursor.Definition.IsThisDeclarationADefinition,
                nativeLinkage = function.linkageKind switch
                {
                    CppLinkageKind.NoLinkage => NativeLinkageKind.None,
                    CppLinkageKind.Internal => NativeLinkageKind.Internal,
                    CppLinkageKind.UniqueExternal => NativeLinkageKind.UniqueExternal,
                    CppLinkageKind.External => NativeLinkageKind.External,
                    _ => NativeLinkageKind.Unknown
                },
                isDeleted = function.flags.HasFlag(CppFunctionFlags.Deleted),
                linkage = m_request.language is SourceLanguage.C || function.isExternC ? "C" : "C++",
                referenceQualifier = function.cursor.Type.CXXRefQualifier switch
                {
                    CXRefQualifierKind.CXRefQualifier_None => NativeReferenceQualifier.None,
                    CXRefQualifierKind.CXRefQualifier_LValue => NativeReferenceQualifier.LValue,
                    CXRefQualifierKind.CXRefQualifier_RValue => NativeReferenceQualifier.RValue,
                    _ => NativeReferenceQualifier.Unknown
                },
                isConst = function.isConst,
                isVolatile = !function.isStatic && function.cursor.Kind is CXCursorKind.CXCursor_CXXMethod or CXCursorKind.CXCursor_ConversionFunction
                    && function.cursor.ThisType.PointeeType.IsVolatileQualified,
                isNoThrow = NoThrowContract(function),
                isVirtual = function.isVirtual,
                isAbstract = function.isPureVirtual,
                isVariadic = function.flags.HasFlag(CppFunctionFlags.Variadic),
                hasPrototype = function.cursor.Type.kind != CXTypeKind.CXType_FunctionNoProto,
                addressability = m_compilation!.callableAddressabilityFacts.GetValueOrDefault(function),
                addressabilityEvidence = m_compilation.callableAddressabilityFacts.ContainsKey(function)
                    ? new(EvidenceLevel.Declared, "frontend:clang:address-expression") : null,
                callingConvention = function.callingConvention.ToString(),
                role = function.isConstructor ? "Constructor" : function.isDestructor ? "Destructor" : "Function",
                templateKind = function.isFunctionTemplate ? "FunctionTemplate" : null,
                templateParameters = function.templateParameters.Select(AddType).ToImmutableArray(),
                attributes = function.attributes.Concat(function.tokenAttributes).Select(attribute => attribute.ToString()).ToImmutableArray()
            };
        }
        if (declaration is CppClass record)
        {
            fact = fact with
            {
                role = record.classKind.ToString(),
                qualifiedName = RecordName(record),
                templateKind = record.templateKind.ToString(),
                templateParameters = record.templateKind == CppTemplateKind.TemplateSpecializedClass ? [] : record.templateParameters.Select(AddType).ToImmutableArray(),
                isDefinition = record.isDefinition,
                isCompleteDefinition = record.isCompleteDefinition,
                isAbstract = record.isAbstract,
                isDefaultConstructible = m_compilation!.defaultConstructionFacts.TryGetValue(record, out bool? constructible) ? constructible : null,
                isStandardLayout = m_compilation.recordStorageTraits.TryGetValue(record, out var traits) ? traits.StandardLayout : null,
                isTriviallyCopyable = m_compilation.recordStorageTraits.TryGetValue(record, out traits) ? traits.TriviallyCopyable : null,
                attributes = record.attributes.Concat(record.tokenAttributes).Select(attribute => attribute.ToString()).ToImmutableArray(),
                baseClasses = record.baseTypes.Select(baseType => new NativeBaseClass(AddType(baseType.type),
                    baseType.visibility.ToString(), baseType.isVirtual)).ToImmutableArray(),
                templateArguments = record.templateSpecializedArguments.Select(argument => new NativeTemplateArgument(
                    argument.argKind.ToString(), argument.argString, argument.argAsType is null ? null : AddType(argument.argAsType))).ToImmutableArray()
            };
        }
        if (declaration is CppField fieldFact)
            fact = fact with
            {
                role = fieldFact.cursor.Kind == CXCursorKind.CXCursor_VarDecl ? "Variable" : fact.role,
                value = fieldFact.initExpression?.ToString() ?? fieldFact.initValue?.ToString(),
                offset = fieldFact.bitOffset >= 0 ? fieldFact.offset : null,
                bitOffset = fieldFact.bitOffset >= 0 ? fieldFact.bitOffset : null,
                bitWidth = fieldFact.isBitField ? fieldFact.bitFieldWidth : null,
                isStatic = fieldFact.storageQualifier == CppStorageQualifier.Static
            };
        if (declaration is CppEnumItem enumItem)
            fact = fact with
            {
                value = enumItem.valueExpression?.ToString() ?? enumItem.value.ToString(CultureInfo.InvariantCulture),
                evaluatedIntegralValue = EvaluateEnumValue(enumItem)
            };
        if (declaration is CppParameter parameterFact)
            fact = fact with { value = parameterFact.initExpression?.ToString() ?? parameterFact.initValue?.ToString() };
        if (declaration is CppMacro macro)
            fact = fact with
            {
                value = macro.value,
                macro = new(macro.parameters is not null, macro.parameters?.ToImmutableArray() ?? [],
                m_compilation!.macroActiveFacts.TryGetValue(macro, out bool active) ? active : null)
                {
                    functionAlias = m_compilation.macroFunctionAliases.TryGetValue(macro, out CppFunction? aliasTarget) ? AddDeclaration(aliasTarget, null) : null,
                    functionAliasEvidence = m_compilation.macroFunctionAliases.ContainsKey(macro)
                        ? new(EvidenceLevel.Declared, "clang.macro-initializer-reference:" + macro.name) : null
                }
            };
        if (m_compilation!.constantFacts.TryGetValue(element, out NativeScalarConstant? constant))
            fact = fact with { constantValue = constant };
        if (m_compilation.stringConstantFacts.TryGetValue(element, out NativeStringConstant? text))
            fact = fact with { stringConstant = text };
        if (m_compilation.functionReferenceFacts.TryGetValue(element, out CppFunction? referencedFunction))
            fact = fact with
            {
                functionReference = AddDeclaration(referencedFunction, null),
                functionReferenceEvidence = new(EvidenceLevel.Declared, "clang.constant-initializer-reference:" + name)
            };
        m_declarations.Add(identity, fact);
        if (element.cursor.kind != 0) m_declarationCursors.Add(id, element.cursor);
        return id;
    }

    private static IEnumerable<ICppDeclaration> DeclarationChildren(ICppContainer container) => container.children;

    private static string EvaluateEnumValue(CppEnumItem item)
    {
        CXTypeKind kind = item.cursor.SemanticParent.EnumDecl_IntegerType.CanonicalType.kind;
        bool unsigned = kind is CXTypeKind.CXType_Char_U or CXTypeKind.CXType_UChar or CXTypeKind.CXType_UShort
            or CXTypeKind.CXType_UInt or CXTypeKind.CXType_ULong or CXTypeKind.CXType_ULongLong;
        return unsigned ? clang.getEnumConstantDeclUnsignedValue(item.cursor).ToString(CultureInfo.InvariantCulture)
            : item.value.ToString(CultureInfo.InvariantCulture);
    }

    private SourceLocation GetLocation(CppElement element)
    {
        CppSourceLocation source = element.span.start;
        if (string.IsNullOrEmpty(source.file) && element.cursor.kind != 0) source = element.cursor.GetSourceLocation();
        if (m_request.usePresumedSourceLocations && element.cursor.kind != 0)
            source = element.cursor.Location.ToPresumedSourceLocation();
        string file = source.file ?? string.Empty;
        if (m_sourcePaths.TryMapPath(file, out string logical)) return new(logical, source.line, source.column);
        if (Path.IsPathRooted(file) && !string.IsNullOrEmpty(m_request.pathBase))
            file = Path.GetRelativePath(m_request.pathBase, file);
        return new(file.Replace('\\', '/'), source.line, source.column);
    }

    private SourceLocation CursorLocation(CXCursor cursor)
    {
        CppSourceLocation source = m_request.usePresumedSourceLocations ? cursor.Location.ToPresumedSourceLocation() : cursor.Extent.Start.ToSourceLocation();
        string file = source.file ?? string.Empty;
        if (m_sourcePaths.TryMapPath(file, out string logical)) return new(logical, source.line, source.column);
        if (Path.IsPathRooted(file) && !string.IsNullOrEmpty(m_request.pathBase)) file = Path.GetRelativePath(m_request.pathBase, file);
        return new(file.Replace('\\', '/'), source.line, source.column);
    }

    private string Identity(
        string category,
        CppElement element,
        string fallback
    ) {
        fallback = m_sourcePaths.MapCompilerProvenance(fallback);
        using var nativeUsr = ClangSharp.Interop.clang.getCursorUSR(element.cursor);
        string usr = element.cursor.kind == 0 ? string.Empty : m_sourcePaths.MapUsr(nativeUsr.ToString(), element.cursor.GetSourceLocation().file ?? string.Empty, m_request.usePresumedSourceLocations);
        string owner = element.cursor.kind == 0 ? string.Empty : CXUtil.GetSemanticRecordIdentity(element.cursor, m_sourcePaths);
        string key = (usr.Length > 0 ? usr + "|" + fallback : fallback + "|" + GetLocation(element)) + owner;
        return category + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private bool? NoThrowContract(CppFunction function)
    {
        if (function.cursor.kind == 0 || function.cursor.Type.ExceptionSpecificationType is
            CXCursor_ExceptionSpecificationKind.CXCursor_ExceptionSpecificationKind_Unevaluated or
            CXCursor_ExceptionSpecificationKind.CXCursor_ExceptionSpecificationKind_Uninstantiated or
            CXCursor_ExceptionSpecificationKind.CXCursor_ExceptionSpecificationKind_Unparsed) return null;
        if (!NativeCallableExceptionFactReader.IsBound(function)) return null;
        return function.cursor.Type.ExceptionSpecificationType switch
        {
            CXCursor_ExceptionSpecificationKind.CXCursor_ExceptionSpecificationKind_BasicNoexcept or
            CXCursor_ExceptionSpecificationKind.CXCursor_ExceptionSpecificationKind_DynamicNone or
            CXCursor_ExceptionSpecificationKind.CXCursor_ExceptionSpecificationKind_NoThrow => true,
            CXCursor_ExceptionSpecificationKind.CXCursor_ExceptionSpecificationKind_None or
            CXCursor_ExceptionSpecificationKind.CXCursor_ExceptionSpecificationKind_Dynamic or
            CXCursor_ExceptionSpecificationKind.CXCursor_ExceptionSpecificationKind_MSAny => false,
            CXCursor_ExceptionSpecificationKind.CXCursor_ExceptionSpecificationKind_ComputedNoexcept
                when m_compilation!.callableExceptionFacts.TryGetValue(function, out bool result) => result,
            _ => null
        };
    }

    private string RecordName(CppClass record)
    {
        if (m_request.language == SourceLanguage.Cpp && !record.isAnonymous && m_compilation!.storageFacts.TryGetValue(record, out NativeStorageFacts storage)
            && !string.IsNullOrWhiteSpace(storage.TypeSpelling) && !storage.TypeSpelling.Contains("(anonymous", StringComparison.Ordinal))
        {
            string spelling = storage.TypeSpelling;
            foreach (string prefix in new[] { "struct ", "class ", "union " })
                if (spelling.StartsWith(prefix, StringComparison.Ordinal)) return m_sourcePaths.MapCompilerProvenance(spelling[prefix.Length..]);
            return m_sourcePaths.MapCompilerProvenance(spelling);
        }
        return m_sourcePaths.MapCompilerProvenance(record.fullName);
    }

    private string QualifiedMemberName(
        CppElement element,
        string fallback
    ) {
        if (m_request.language != SourceLanguage.Cpp || element.cursor.kind == 0 || element is not ICppMember member) return fallback;
        CXCursor owner = element.cursor.SemanticParent;
        if (element is CppEnumItem && owner.Kind == CXCursorKind.CXCursor_EnumDecl)
        {
            if (owner.EnumDecl_IsScoped)
            {
                string enumeration = CXUtil.GetTypeSpelling(owner.Type.CanonicalType);
                if (string.IsNullOrWhiteSpace(enumeration) || enumeration.Contains("(anonymous", StringComparison.Ordinal))
                    throw new InvalidOperationException("A scoped native enumeration omitted its compiler-qualified type spelling.");
                if (enumeration.StartsWith("enum ", StringComparison.Ordinal)) enumeration = enumeration[5..];
                return m_sourcePaths.MapCompilerProvenance(enumeration + "::" + member.name);
            }
            // Unscoped and anonymous enum values are injected into the enum's
            // enclosing scope. The generic fullParentName walk stops at enums.
            if (element.parent is CppEnum enumerationOwner)
                fallback = string.IsNullOrEmpty(enumerationOwner.fullParentName) ? member.name : enumerationOwner.fullParentName + "::" + member.name;
            owner = owner.SemanticParent;
        }
        if (owner.Kind is not (CXCursorKind.CXCursor_ClassDecl or CXCursorKind.CXCursor_StructDecl or CXCursorKind.CXCursor_UnionDecl)
            || owner.IsAnonymous || owner.Type.kind == CXTypeKind.CXType_Invalid) return fallback;
        string spelling = CXUtil.GetTypeSpelling(owner.Type.CanonicalType);
        if (string.IsNullOrWhiteSpace(spelling) || spelling.Contains("(anonymous", StringComparison.Ordinal)) return fallback;
        foreach (string prefix in new[] { "struct ", "class ", "union " })
            if (spelling.StartsWith(prefix, StringComparison.Ordinal)) { spelling = spelling[prefix.Length..]; break; }
        return m_sourcePaths.MapCompilerProvenance(spelling + "::" + member.name);
    }
}
