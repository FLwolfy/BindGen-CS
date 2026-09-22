namespace BGCS.Platform;

using BGCS.CppAst.Model.Types;

internal static class NativeAbi
{
    internal static string GetPrimitiveTypeName(CppPrimitiveType primitiveType)
    {
        return primitiveType.Kind switch
        {
            CppPrimitiveKind.Void => "void",
            CppPrimitiveKind.Bool => "bool",
            CppPrimitiveKind.WChar => primitiveType.SizeOf switch
            {
                2 => "char",
                4 => "int",
                _ => throw UnsupportedSize(primitiveType)
            },
            CppPrimitiveKind.Char or CppPrimitiveKind.UnsignedChar => "byte",
            CppPrimitiveKind.Short => "short",
            CppPrimitiveKind.Int => "int",
            CppPrimitiveKind.Long => primitiveType.SizeOf == 8 ? "long" : "int",
            CppPrimitiveKind.LongLong => "long",
            CppPrimitiveKind.UnsignedShort => "ushort",
            CppPrimitiveKind.UnsignedInt => "uint",
            CppPrimitiveKind.UnsignedLong => primitiveType.SizeOf == 8 ? "ulong" : "uint",
            CppPrimitiveKind.UnsignedLongLong => "ulong",
            CppPrimitiveKind.Float => "float",
            CppPrimitiveKind.Double => "double",
            CppPrimitiveKind.LongDouble => primitiveType.SizeOf switch
            {
                8 => "double",
                12 => "NativeLongDouble12",
                16 => "NativeLongDouble16",
                _ => throw UnsupportedSize(primitiveType)
            },
            CppPrimitiveKind.Int128 => "Int128",
            CppPrimitiveKind.UInt128 => "UInt128",
            CppPrimitiveKind.Float16 => "Half",
            CppPrimitiveKind.BFloat16 => "ushort",
            CppPrimitiveKind.ObjCId or CppPrimitiveKind.ObjCSel or CppPrimitiveKind.ObjCClass or CppPrimitiveKind.ObjCObject => "nint",
            _ => throw new NotSupportedException($"Primitive type '{primitiveType.Kind}' is not supported by the native ABI mapper.")
        };
    }

    private static NotSupportedException UnsupportedSize(CppPrimitiveType primitiveType)
    {
        return new($"Primitive type '{primitiveType.Kind}' has unsupported ABI size {primitiveType.SizeOf} bytes.");
    }
}
