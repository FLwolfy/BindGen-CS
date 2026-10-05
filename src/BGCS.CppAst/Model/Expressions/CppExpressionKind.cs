// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
namespace BGCS.CppAst.Model.Expressions;

/// <summary>
/// Classifies parsed native expression syntax before lowering into binding facts.
/// </summary>
public enum CppExpressionKind
{
    /// <summary>
    /// An expression whose category is unavailable.
    /// </summary>
    Unknown,
    /// <summary>
    /// An expression retained as tokens without detailed structure.
    /// </summary>
    Unexposed,
    /// <summary>
    /// A reference to a declaration.
    /// </summary>
    DeclRef,
    /// <summary>
    /// A reference to a member.
    /// </summary>
    MemberRef,
    /// <summary>
    /// A callable invocation.
    /// </summary>
    Call,
    /// <summary>
    /// An Objective-C message expression.
    /// </summary>
    ObjCMessage,
    /// <summary>
    /// A block literal expression.
    /// </summary>
    Block,
    /// <summary>
    /// An integral literal.
    /// </summary>
    IntegerLiteral,
    /// <summary>
    /// A floating-point literal.
    /// </summary>
    FloatingLiteral,
    /// <summary>
    /// An imaginary numeric literal.
    /// </summary>
    ImaginaryLiteral,
    /// <summary>
    /// A native string literal.
    /// </summary>
    StringLiteral,
    /// <summary>
    /// A native character literal.
    /// </summary>
    CharacterLiteral,
    /// <summary>
    /// A parenthesized expression.
    /// </summary>
    Paren,
    /// <summary>
    /// A unary operator expression.
    /// </summary>
    UnaryOperator,
    /// <summary>
    /// An indexed element access.
    /// </summary>
    ArraySubscript,
    /// <summary>
    /// A binary operator expression.
    /// </summary>
    BinaryOperator,
    /// <summary>
    /// A combined arithmetic and assignment expression.
    /// </summary>
    CompoundAssignOperator,
    /// <summary>
    /// A three-operand conditional expression.
    /// </summary>
    ConditionalOperator,
    /// <summary>
    /// A C-style type cast.
    /// </summary>
    CStyleCast,
    /// <summary>
    /// A C compound value literal.
    /// </summary>
    CompoundLiteral,
    /// <summary>
    /// A brace-enclosed initialization list.
    /// </summary>
    InitList,
    /// <summary>
    /// The address of a statement label.
    /// </summary>
    AddrLabel,
    /// <summary>
    /// A GNU statement expression.
    /// </summary>
    Stmt,
    /// <summary>
    /// A C generic selection expression.
    /// </summary>
    GenericSelection,
    /// <summary>
    /// The GNU null pointer extension.
    /// </summary>
    GNUNull,
    /// <summary>
    /// A C++ static cast.
    /// </summary>
    CXXStaticCast,
    /// <summary>
    /// A C++ runtime-checked cast.
    /// </summary>
    CXXDynamicCast,
    /// <summary>
    /// A C++ representation cast.
    /// </summary>
    CXXReinterpretCast,
    /// <summary>
    /// A C++ qualification cast.
    /// </summary>
    CXXConstCast,
    /// <summary>
    /// A C++ functional type conversion.
    /// </summary>
    CXXFunctionalCast,
    /// <summary>
    /// A C++ runtime type information expression.
    /// </summary>
    CXXTypeid,
    /// <summary>
    /// A C++ boolean literal.
    /// </summary>
    CXXBoolLiteral,
    /// <summary>
    /// A C++ null pointer literal.
    /// </summary>
    CXXNullPtrLiteral,
    /// <summary>
    /// A C++ current-object pointer expression.
    /// </summary>
    CXXThis,
    /// <summary>
    /// A C++ exception throw expression.
    /// </summary>
    CXXThrow,
    /// <summary>
    /// A C++ allocation expression.
    /// </summary>
    CXXNew,
    /// <summary>
    /// A C++ deallocation expression.
    /// </summary>
    CXXDelete,
    /// <summary>
    /// A unary type or expression trait, including sizeof.
    /// </summary>
    Unary,
    /// <summary>
    /// An Objective-C string literal.
    /// </summary>
    ObjCStringLiteral,
    /// <summary>
    /// An Objective-C type encoding expression.
    /// </summary>
    ObjCEncode,
    /// <summary>
    /// An Objective-C selector expression.
    /// </summary>
    ObjCSelector,
    /// <summary>
    /// An Objective-C protocol expression.
    /// </summary>
    ObjCProtocol,
    /// <summary>
    /// An Objective-C ownership bridge cast.
    /// </summary>
    ObjCBridgedCast,
    /// <summary>
    /// A C++ template parameter pack expansion.
    /// </summary>
    PackExpansion,
    /// <summary>
    /// A C++ template parameter pack size query.
    /// </summary>
    SizeOfPack,
    /// <summary>
    /// A C++ lambda expression.
    /// </summary>
    Lambda,
    /// <summary>
    /// An Objective-C boolean literal.
    /// </summary>
    ObjCBoolLiteral,
    /// <summary>
    /// An Objective-C current-object expression.
    /// </summary>
    ObjCSelf,
    /// <summary>
    /// An OpenMP array shaping expression.
    /// </summary>
    OMPArrayShapingExpr,
    /// <summary>
    /// An Objective-C platform availability expression.
    /// </summary>
    ObjCAvailabilityCheck,
    /// <summary>
    /// A fixed-point numeric literal.
    /// </summary>
    FixedPointLiteral,
}
