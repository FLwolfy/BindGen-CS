using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Expressions;
/// <summary>
/// Defines values for <c>CppExpressionKind</c>.
/// </summary>
public enum CppExpressionKind
{
    Unknown,
    Unexposed,
    DeclRef,
    MemberRef,
    Call,
    ObjCMessage,
    Block,
    IntegerLiteral,
    FloatingLiteral,
    ImaginaryLiteral,
    StringLiteral,
    CharacterLiteral,
    Paren,
    UnaryOperator,
    ArraySubscript,
    BinaryOperator,
    CompoundAssignOperator,
    ConditionalOperator,
    CStyleCast,
    CompoundLiteral,
    InitList,
    AddrLabel,
    Stmt,
    GenericSelection,
    GNUNull,
    CXXStaticCast,
    CXXDynamicCast,
    CXXReinterpretCast,
    CXXConstCast,
    CXXFunctionalCast,
    CXXTypeid,
    CXXBoolLiteral,
    CXXNullPtrLiteral,
    CXXThis,
    CXXThrow,
    CXXNew,
    CXXDelete,
    Unary,
    ObjCStringLiteral,
    ObjCEncode,
    ObjCSelector,
    ObjCProtocol,
    ObjCBridgedCast,
    PackExpansion,
    SizeOfPack,
    Lambda,
    ObjCBoolLiteral,
    ObjCSelf,
    OMPArrayShapingExpr,
    ObjCAvailabilityCheck,
    FixedPointLiteral,
}
