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
    private readonly CsCodeGeneratorConfig config;
    private readonly Dictionary<CppType, string> managedAliases = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, ReferencedRecord> referencedRecords = new(StringComparer.Ordinal);

    public TypeAnalyzer(CsCodeGeneratorConfig config)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
    }

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
                    current = pointer.ElementType;
                    continue;
                case CppReferenceType reference:
                    pointerDepth++;
                    current = reference.ElementType;
                    continue;
                case CppQualifiedType qualified:
                    isConst |= qualified.Qualifier == CppTypeQualifier.Const;
                    current = qualified.ElementType;
                    continue;
                case CppArrayType array:
                    current = array.ElementType;
                    continue;
            }
            break;
        }

        string managedName = config.TypeConverter.Convert(type, CsTypeStyle.Raw);
        if (managedAliases.TryGetValue(current, out string? managedAlias))
            managedName = managedAlias + new string('*', pointerDepth);
        else if (current is CppTypedef typedef && IsVoidPointerAlias(typedef))
            managedName = "nint" + new string('*', pointerDepth);
        else if (current is CppTypedef voidAlias && IsVoidAlias(voidAlias))
            managedName = config.GetManagedTypeName(voidAlias.Name) + new string('*', pointerDepth);
        if (current is CppPrimitiveType { Kind: CppPrimitiveKind.Bool })
            managedName = config.GetBoolType() + new string('*', pointerDepth);
        TrackReferencedRecord(type, managedName);
        return new(type.GetDisplayName(), managedName, pointerDepth, isConst, type.SizeOf);
    }

    internal IReadOnlyCollection<ReferencedRecord> ReferencedRecords => referencedRecords.Values;

    internal void RegisterManagedAlias(CppType nativeType, string managedName)
    {
        ArgumentNullException.ThrowIfNull(nativeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedName);
        managedAliases[nativeType] = managedName;
    }

    private void TrackReferencedRecord(CppType type, string managedName)
    {
        CppType current = type;
        bool behindPointer = managedName.TrimEnd().EndsWith('*');
        while (true)
        {
            switch (current)
            {
                case CppQualifiedType qualified:
                    current = qualified.ElementType;
                    continue;
                case CppTypedef typedef:
                    current = typedef.ElementType;
                    continue;
                case CppPointerType pointer:
                    behindPointer = true;
                    current = pointer.ElementType;
                    continue;
                case CppReferenceType reference:
                    behindPointer = true;
                    current = reference.ElementType;
                    continue;
                case CppArrayType array:
                    current = array.ElementType;
                    continue;
            }
            break;
        }
        if (current is not CppClass record)
            return;
        string name = managedName.Trim();
        while (name.EndsWith('*'))
            name = name[..^1].TrimEnd();
        if (string.IsNullOrWhiteSpace(name) || name.Contains(' ') ||
            !string.Equals(name, config.GetManagedTypeName(record.Name), StringComparison.Ordinal))
            return;
        if (referencedRecords.TryGetValue(name, out ReferencedRecord? existing))
        {
            if (!behindPointer && existing.BehindPointerOnly)
                referencedRecords[name] = existing with { BehindPointerOnly = false };
            return;
        }
        referencedRecords.Add(name, new(record.FullName, name, Math.Max(0, record.SizeOf),
            Math.Clamp(record.AlignOf, 1, 8), behindPointer));
    }

    private static bool IsVoidPointerAlias(CppTypedef typedef)
    {
        CppType current = typedef.ElementType;
        while (current is CppQualifiedType qualified)
            current = qualified.ElementType;
        while (current is CppTypedef nested)
        {
            current = nested.ElementType;
            while (current is CppQualifiedType qualified)
                current = qualified.ElementType;
        }
        if (current is not CppPointerType pointer)
            return false;
        current = pointer.ElementType;
        while (current is CppQualifiedType qualified)
            current = qualified.ElementType;
        return current is CppPrimitiveType { Kind: CppPrimitiveKind.Void };
    }

    private static bool IsVoidAlias(CppTypedef typedef)
    {
        CppType current = typedef.ElementType;
        while (true)
        {
            if (current is CppQualifiedType qualified)
            {
                current = qualified.ElementType;
                continue;
            }
            if (current is CppTypedef nested)
            {
                current = nested.ElementType;
                continue;
            }
            break;
        }
        return current is CppPrimitiveType { Kind: CppPrimitiveKind.Void };
    }

    internal sealed record ReferencedRecord(string NativeName, string ManagedName, int Size, int Alignment,
        bool BehindPointerOnly);
}
