using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Configuration;
using BGCS.CppAst.Extensions;

namespace BGCS.Analysis;

using BGCS.Conversion;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate;

/// <summary>
/// Converts C/C++ AST types into target-ABI-aware intermediate type references.
/// </summary>
public sealed class TypeAnalyzer
{
    private readonly CsCodeGeneratorConfig m_config;
    private readonly Dictionary<CppType, string> m_managedAliases = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, ReferencedRecord> m_referencedRecords = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a target-aware analyzer for one generation attempt.
    /// </summary>
    /// <param name="config">Generation configuration; callers retain ownership and must not mutate it during analysis.</param>
    public TypeAnalyzer(CsCodeGeneratorConfig config)
    {
        this.m_config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Lowers one borrowed AST type into a target ABI type reference.
    /// </summary>
    /// <param name="type">Native type whose owning compilation must remain alive during analysis.</param>
    /// <returns>An AST-independent type reference containing the resolved managed carrier and native layout facts.</returns>
    public BindingTypeReference Analyze(CppType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        int pointerDepth = 0;
        bool isConst = false;
        CppType current = type;
        while (true)
        {
            switch (current)
            {
                case CppPointerType pointer:
                    pointerDepth++;
                    current = pointer.elementType;
                    continue;
                case CppReferenceType reference:
                    pointerDepth++;
                    current = reference.elementType;
                    continue;
                case CppQualifiedType qualified:
                    isConst |= qualified.qualifier == CppTypeQualifier.Const;
                    current = qualified.elementType;
                    continue;
                case CppArrayType array:
                    current = array.elementType;
                    continue;
            }

            break;
        }

        string managedName = this.m_config.typeConverter.Convert(type, CsTypeStyle.Raw);
        if (!this.m_config.delegatesAsVoidPointer && type.IsDelegate(out CppFunctionType? callback))
            managedName = GetCallbackPointerType(callback!);
        if (this.m_managedAliases.TryGetValue(current, out string? managedAlias))
            managedName = managedAlias + new string('*', pointerDepth);
        else if (current is CppTypedef typedef && IsVoidPointerAlias(typedef))
            managedName = "nint" + new string('*', pointerDepth);
        else if (current is CppTypedef voidAlias && IsVoidAlias(voidAlias))
            managedName = this.m_config.GetManagedTypeName(voidAlias.name) + new string('*', pointerDepth);
        if (current is CppPrimitiveType { kind: CppPrimitiveKind.Bool })
            managedName = this.m_config.GetBoolType() + new string('*', pointerDepth);
        if (this.m_config.generateHandles && pointerDepth > 0 && IsIncompleteRecord(current) && managedName.EndsWith('*'))
        {
            // An incomplete record is emitted as an nint-backed opaque handle. One native
            // pointer indirection is therefore represented by the handle value itself;
            // additional indirections remain explicit (T** -> Handle*).
            managedName = managedName[..^1].TrimEnd();
        }

        TrackReferencedRecord(type, managedName);
        return new(type.GetDisplayName(), managedName, pointerDepth, isConst, type.sizeOf);
    }

    internal IReadOnlyCollection<ReferencedRecord> referencedRecords => this.m_referencedRecords.Values;

    internal void RegisterManagedAlias(
        CppType nativeType,
        string managedName
    ) {
        ArgumentNullException.ThrowIfNull(nativeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedName);
        this.m_managedAliases[nativeType] = managedName;
    }

    private string GetCallbackPointerType(CppFunctionType callback)
    {
        IEnumerable<string> carriers = callback.parameters.Select(parameter => GetCallbackCarrier(parameter.type)).Append(GetCallbackCarrier(callback.returnType));
        return $"delegate* unmanaged[{callback.callingConvention.GetCallingConventionDelegate()}]<{string.Join(", ", carriers)}>";
    }

    private string GetCallbackCarrier(CppType type)
    {
        string managedName = Analyze(type).managedName;
        if (!this.m_config.generateHandles)
            return managedName;
        int pointerDepth = 0;
        CppType current = type;
        while (true)
        {
            switch (current)
            {
                case CppQualifiedType qualified:
                    current = qualified.elementType;
                    continue;
                case CppTypedef typedef:
                    current = typedef.elementType;
                    continue;
                case CppPointerType pointer:
                    pointerDepth++;
                    current = pointer.elementType;
                    continue;
                case CppReferenceType reference:
                    pointerDepth++;
                    current = reference.elementType;
                    continue;
            }

            break;
        }

        // Callback ABI signatures carry native pointers, independently of the managed handle's alias.
        return pointerDepth > 0 && current is CppClass { isDefinition: false } ? "nint" + new string('*', pointerDepth - 1) : managedName;
    }

    private void TrackReferencedRecord(
        CppType type,
        string managedName
    ) {
        CppType current = type;
        bool behindPointer = managedName.TrimEnd().EndsWith('*');
        while (true)
        {
            switch (current)
            {
                case CppQualifiedType qualified:
                    current = qualified.elementType;
                    continue;
                case CppTypedef typedef:
                    current = typedef.elementType;
                    continue;
                case CppPointerType pointer:
                    behindPointer = true;
                    current = pointer.elementType;
                    continue;
                case CppReferenceType reference:
                    behindPointer = true;
                    current = reference.elementType;
                    continue;
                case CppArrayType array:
                    current = array.elementType;
                    continue;
            }

            break;
        }

        if (current is not CppClass record)
            return;
        string name = managedName.Trim();
        while (name.EndsWith('*'))
            name = name[..^1].TrimEnd();
        if (string.IsNullOrWhiteSpace(name) || name.Contains(' ') || !string.Equals(name, this.m_config.GetManagedTypeName(record.name), StringComparison.Ordinal))
            return;
        if (this.m_referencedRecords.TryGetValue(name, out ReferencedRecord? existing))
        {
            if (!behindPointer && existing.behindPointerOnly)
                this.m_referencedRecords[name] = existing with
                {
                    behindPointerOnly = false
                };
            return;
        }

        this.m_referencedRecords.Add(name, new(record.fullName, name, Math.Max(0, record.sizeOf), Math.Max(1, record.alignOf), behindPointer));
    }

    private static bool IsVoidPointerAlias(CppTypedef typedef)
    {
        CppType current = typedef.elementType;
        while (current is CppQualifiedType qualified)
            current = qualified.elementType;
        while (current is CppTypedef nested)
        {
            current = nested.elementType;
            while (current is CppQualifiedType qualified)
                current = qualified.elementType;
        }

        if (current is not CppPointerType pointer)
            return false;
        current = pointer.elementType;
        while (current is CppQualifiedType qualified)
            current = qualified.elementType;
        return current is CppPrimitiveType { kind: CppPrimitiveKind.Void };
    }

    private static bool IsVoidAlias(CppTypedef typedef)
    {
        CppType current = typedef.elementType;
        while (true)
        {
            if (current is CppQualifiedType qualified)
            {
                current = qualified.elementType;
                continue;
            }

            if (current is CppTypedef nested)
            {
                current = nested.elementType;
                continue;
            }

            break;
        }

        return current is CppPrimitiveType { kind: CppPrimitiveKind.Void };
    }

    private static bool IsIncompleteRecord(CppType type)
    {
        while (true)
        {
            switch (type)
            {
                case CppQualifiedType qualified:
                    type = qualified.elementType;
                    continue;
                case CppTypedef typedef when !typedef.IsOpaqueHandle():
                    type = typedef.elementType;
                    continue;
                default:
                    return type is CppClass { isDefinition: false };
            }
        }
    }

    internal sealed record ReferencedRecord(
        string nativeName,
        string managedName,
        int size,
        int alignment,
        bool behindPointerOnly
    );
}
