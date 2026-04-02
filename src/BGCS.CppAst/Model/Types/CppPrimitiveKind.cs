using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Types;
/// <summary>
/// C++ primitive kinds used by <see cref="CppPrimitiveType"/>
/// </summary>
public enum CppPrimitiveKind
{
    /// <summary>
    /// C++ `void`
    /// </summary>
    Void,

    /// <summary>
    /// C++ `bool`
    /// </summary>
    Bool,

    /// <summary>
    /// C++ `wchar`
    /// </summary>
    WChar,

    /// <summary>
    /// C++ `char`
    /// </summary>
    Char,

    /// <summary>
    /// C++ `short`
    /// </summary>
    Short,

    /// <summary>
    /// C++ `int`
    /// </summary>
    Int,

    /// <summary>
    /// C++ `long`
    /// </summary>
    Long,
    
    /// <summary>
    /// C++ `long long` (64bits)
    /// </summary>
    LongLong,

    /// <summary>
    /// C++ `unsigned char`
    /// </summary>
    UnsignedChar,

    /// <summary>
    /// C++ `unsigned short`
    /// </summary>
    UnsignedShort,

    /// <summary>
    /// C++ `unsigned int`
    /// </summary>
    UnsignedInt,

    /// <summary>
    /// C++ `unsigned long`
    /// </summary>
    UnsignedLong,

    /// <summary>
    /// C++ `unsigned long long` (64 bits)
    /// </summary>
    UnsignedLongLong,

    /// <summary>
    /// C++ `float`
    /// </summary>
    Float,

    /// <summary>
    /// C++ `double`
    /// </summary>
    Double,

    /// <summary>
    /// C++ `long double`
    /// </summary>
    LongDouble,
    
    /// <summary>
    /// An Objective-C `id` type.
    /// </summary>
    ObjCId,
    
    /// <summary>
    /// An Objective-C `SEL` type.
    /// </summary>
    ObjCSel,
    
    /// <summary>
    /// An Objective-C `Class` type.
    /// </summary>
    ObjCClass,
    
    /// <summary>
    /// An Objective-C `NSObject` type.
    /// </summary>
    ObjCObject,
    
    /// <summary>
    /// Unsigned 128 bits integer type.
    /// </summary>
    Int128,
    
    /// <summary>
    /// 128 bits integer type.
    /// </summary>
    UInt128,
    
    /// <summary>
    /// A 16 bits floating point type.
    /// </summary>
    Float16,
    
    /// <summary>
    /// A 16 bits brain float type.
    /// </summary>
    BFloat16,
}
