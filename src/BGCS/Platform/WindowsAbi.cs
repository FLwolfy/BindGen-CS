namespace BGCS.Platform;

using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;

internal static class WindowsAbi
{
    internal static CppTargetCpu GetTargetCpu(WindowsTargetArchitecture architecture)
    {
        return architecture switch
        {
            WindowsTargetArchitecture.X86 => CppTargetCpu.X86,
            WindowsTargetArchitecture.X64 => CppTargetCpu.X86_64,
            WindowsTargetArchitecture.Arm64 => CppTargetCpu.ARM64,
            _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture, null)
        };
    }

    internal static string GetPrimitiveTypeName(CppPrimitiveKind kind)
    {
        return kind switch
        {
            CppPrimitiveKind.Void => "void",
            CppPrimitiveKind.Bool => "bool",
            CppPrimitiveKind.WChar => "char",
            CppPrimitiveKind.Char => "byte",
            CppPrimitiveKind.Short => "short",
            CppPrimitiveKind.Int => "int",
            CppPrimitiveKind.Long => "int",
            CppPrimitiveKind.LongLong => "long",
            CppPrimitiveKind.UnsignedChar => "byte",
            CppPrimitiveKind.UnsignedShort => "ushort",
            CppPrimitiveKind.UnsignedInt => "uint",
            CppPrimitiveKind.UnsignedLong => "uint",
            CppPrimitiveKind.UnsignedLongLong => "ulong",
            CppPrimitiveKind.Float => "float",
            CppPrimitiveKind.Double => "double",
            CppPrimitiveKind.LongDouble => "double",
            CppPrimitiveKind.Int128 => "Int128",
            CppPrimitiveKind.UInt128 => "UInt128",
            CppPrimitiveKind.Float16 => "Half",
            CppPrimitiveKind.BFloat16 => "ushort",
            CppPrimitiveKind.ObjCId or CppPrimitiveKind.ObjCSel or CppPrimitiveKind.ObjCClass or CppPrimitiveKind.ObjCObject => "nint",
            _ => throw new NotSupportedException($"Primitive type '{kind}' is not supported by the Windows ABI mapper.")
        };
    }
}
