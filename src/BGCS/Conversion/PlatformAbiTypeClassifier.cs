namespace BGCS.Conversion;

using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;

/// <summary>
/// Recognizes compiler ABI carrier types whose source spelling is not a portable managed contract.
/// </summary>
internal static class PlatformAbiTypeClassifier
{
    internal const string Aapcs64VaListManagedType = "global::BGCS.Runtime.Aapcs64VaList";

    internal static bool TryGetManagedCarrier(CppType type, out string managedType)
    {
        if (IsDecayedVaList(type))
        {
            managedType = "nint";
            return true;
        }

        if (IsAapcs64VaList(type))
        {
            managedType = Aapcs64VaListManagedType;
            return true;
        }

        managedType = string.Empty;
        return false;
    }

    internal static bool IsVaList(CppType type) => IsDecayedVaList(type) || IsAapcs64VaList(type);

    /// <summary>
    /// Detects the SysV x64 <c>va_list</c> representation: an array of one compiler-owned
    /// <c>__va_list_tag</c>. C function parameters decay this array to a pointer, so the portable
    /// managed ABI representation is one native integer. Struct-based <c>va_list</c> ABIs are not
    /// matched and therefore cannot be accidentally lowered as pointers.
    /// </summary>
    internal static bool IsDecayedVaList(CppType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        while (type is CppTypedef typedef)
            type = typedef.ElementType;
        while (type is CppQualifiedType qualified)
            type = qualified.ElementType;
        if (type is not CppArrayType { Size: 1 } array)
            return false;
        type = array.ElementType;
        while (type is CppTypedef typedef)
            type = typedef.ElementType;
        while (type is CppQualifiedType qualified)
            type = qualified.ElementType;
        return type is CppClass cppClass &&
            string.Equals(cppClass.Name, "__va_list_tag", StringComparison.Ordinal);
    }

    /// <summary>
    /// Detects the AAPCS64 value representation defined by the Arm procedure-call standard.
    /// This must remain a five-field blittable structure: unlike the SysV x64 array form, it
    /// does not decay to a pointer when passed to a native function.
    /// </summary>
    internal static bool IsAapcs64VaList(CppType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        type = Unwrap(type);
        if (type is not CppClass { SizeOf: 32 } cppClass || cppClass.Fields.Count != 5)
            return false;

        string[] names = ["__stack", "__gr_top", "__vr_top", "__gr_offs", "__vr_offs"];
        for (int i = 0; i < names.Length; i++)
        {
            if (!string.Equals(cppClass.Fields[i].Name, names[i], StringComparison.Ordinal))
                return false;
        }

        return IsVoidPointer(cppClass.Fields[0].Type) &&
            IsVoidPointer(cppClass.Fields[1].Type) &&
            IsVoidPointer(cppClass.Fields[2].Type) &&
            IsInt32(cppClass.Fields[3].Type) &&
            IsInt32(cppClass.Fields[4].Type);
    }

    private static CppType Unwrap(CppType type)
    {
        while (type is CppTypedef typedef)
            type = typedef.ElementType;
        while (type is CppQualifiedType qualified)
            type = qualified.ElementType;
        return type;
    }

    private static bool IsVoidPointer(CppType type)
    {
        type = Unwrap(type);
        return type is CppPointerType pointer &&
            Unwrap(pointer.ElementType) is CppPrimitiveType { Kind: CppPrimitiveKind.Void };
    }

    private static bool IsInt32(CppType type)
    {
        type = Unwrap(type);
        return type is CppPrimitiveType { Kind: CppPrimitiveKind.Int, SizeOf: 4 };
    }
}
