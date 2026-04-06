using System;
namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Utilities;

/// <summary>
/// Defines the public class <c>CppModelBuilder</c>.
/// </summary>
public unsafe partial class CppModelBuilder
{
    /// <summary>
    /// Executes public operation <c>VisitExpression</c>.
    /// </summary>
    public static CppExpression? VisitExpression(CXCursor cursor)
    {
        CppExpression? expr = null;
        bool visitChildren = false;
        CppExpressionKind kind = ToExpressionKind(cursor.Kind);

        switch (cursor.Kind)
        {
            case CXCursorKind.CXCursor_IntegerLiteral:
            case CXCursorKind.CXCursor_FloatingLiteral:
            case CXCursorKind.CXCursor_ImaginaryLiteral:
            case CXCursorKind.CXCursor_StringLiteral:
            case CXCursorKind.CXCursor_CharacterLiteral:
            case CXCursorKind.CXCursor_FixedPointLiteral:
                expr = new CppLiteralExpression(cursor, kind, cursor.AsText());
                break;

            case CXCursorKind.CXCursor_ParenExpr:
                expr = new CppParenExpression(cursor);
                visitChildren = true;
                break;

            case CXCursorKind.CXCursor_UnaryOperator:
                var tokens = new Tokenizer(cursor);
                expr = new CppUnaryExpression(cursor, CppExpressionKind.UnaryOperator)
                {
                    Operator = tokens.Count > 0 ? tokens.GetString(0) : string.Empty
                };
                visitChildren = true;
                break;

            case CXCursorKind.CXCursor_BinaryOperator:
                expr = new CppBinaryExpression(cursor, CppExpressionKind.BinaryOperator);
                visitChildren = true;
                break;

            case CXCursorKind.CXCursor_InitListExpr:
                expr = new CppInitListExpression(cursor);
                visitChildren = true;
                break;

            default:
                var rawExpression = new CppRawExpression(cursor, kind);
                rawExpression.AppendTokens(cursor);
                expr = rawExpression;
                break;
        }

        expr.AssignSourceSpan(cursor);

        if (visitChildren)
        {
            using DGCHandle<CppExpression> handle = new(expr);
            cursor.VisitChildren(static (listCursor, initListCursor, clientData) =>
            {
                CppExpression expr = DGCHandle<CppExpression>.ObjFrom(clientData);
                var item = VisitExpression(listCursor);
                if (item != null)
                {
                    expr.AddArgument(item);
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            }, handle);
        }

        if (expr is CppBinaryExpression binaryExpression)
        {
            var beforeOperatorOffset = expr.Arguments[0].Span.End.Offset;
            var afterOperatorOffset = expr.Arguments[1].Span.Start.Offset;
            binaryExpression.Operator = cursor.GetCursorAsTextBetweenOffset(beforeOperatorOffset, afterOperatorOffset);
        }

        return expr;
    }

    /// <summary>
    /// Executes public operation <c>ToExpressionKind</c>.
    /// </summary>
    public static CppExpressionKind ToExpressionKind(CXCursorKind kind) => kind switch
    {
        CXCursorKind.CXCursor_UnexposedExpr => CppExpressionKind.Unexposed,
        CXCursorKind.CXCursor_DeclRefExpr => CppExpressionKind.DeclRef,
        CXCursorKind.CXCursor_MemberRefExpr => CppExpressionKind.MemberRef,
        CXCursorKind.CXCursor_CallExpr => CppExpressionKind.Call,
        CXCursorKind.CXCursor_ObjCMessageExpr => CppExpressionKind.ObjCMessage,
        CXCursorKind.CXCursor_BlockExpr => CppExpressionKind.Block,
        CXCursorKind.CXCursor_IntegerLiteral => CppExpressionKind.IntegerLiteral,
        CXCursorKind.CXCursor_FloatingLiteral => CppExpressionKind.FloatingLiteral,
        CXCursorKind.CXCursor_ImaginaryLiteral => CppExpressionKind.ImaginaryLiteral,
        CXCursorKind.CXCursor_StringLiteral => CppExpressionKind.StringLiteral,
        CXCursorKind.CXCursor_CharacterLiteral => CppExpressionKind.CharacterLiteral,
        CXCursorKind.CXCursor_ParenExpr => CppExpressionKind.Paren,
        CXCursorKind.CXCursor_UnaryOperator => CppExpressionKind.UnaryOperator,
        CXCursorKind.CXCursor_ArraySubscriptExpr => CppExpressionKind.ArraySubscript,
        CXCursorKind.CXCursor_BinaryOperator => CppExpressionKind.BinaryOperator,
        CXCursorKind.CXCursor_CompoundAssignOperator => CppExpressionKind.CompoundAssignOperator,
        CXCursorKind.CXCursor_ConditionalOperator => CppExpressionKind.ConditionalOperator,
        CXCursorKind.CXCursor_CStyleCastExpr => CppExpressionKind.CStyleCast,
        CXCursorKind.CXCursor_CompoundLiteralExpr => CppExpressionKind.CompoundLiteral,
        CXCursorKind.CXCursor_InitListExpr => CppExpressionKind.InitList,
        CXCursorKind.CXCursor_AddrLabelExpr => CppExpressionKind.AddrLabel,
        CXCursorKind.CXCursor_StmtExpr => CppExpressionKind.Stmt,
        CXCursorKind.CXCursor_GenericSelectionExpr => CppExpressionKind.GenericSelection,
        CXCursorKind.CXCursor_GNUNullExpr => CppExpressionKind.GNUNull,
        CXCursorKind.CXCursor_CXXStaticCastExpr => CppExpressionKind.CXXStaticCast,
        CXCursorKind.CXCursor_CXXDynamicCastExpr => CppExpressionKind.CXXDynamicCast,
        CXCursorKind.CXCursor_CXXReinterpretCastExpr => CppExpressionKind.CXXReinterpretCast,
        CXCursorKind.CXCursor_CXXConstCastExpr => CppExpressionKind.CXXConstCast,
        CXCursorKind.CXCursor_CXXFunctionalCastExpr => CppExpressionKind.CXXFunctionalCast,
        CXCursorKind.CXCursor_CXXTypeidExpr => CppExpressionKind.CXXTypeid,
        CXCursorKind.CXCursor_CXXBoolLiteralExpr => CppExpressionKind.CXXBoolLiteral,
        CXCursorKind.CXCursor_CXXNullPtrLiteralExpr => CppExpressionKind.CXXNullPtrLiteral,
        CXCursorKind.CXCursor_CXXThisExpr => CppExpressionKind.CXXThis,
        CXCursorKind.CXCursor_CXXThrowExpr => CppExpressionKind.CXXThrow,
        CXCursorKind.CXCursor_CXXNewExpr => CppExpressionKind.CXXNew,
        CXCursorKind.CXCursor_CXXDeleteExpr => CppExpressionKind.CXXDelete,
        CXCursorKind.CXCursor_UnaryExpr => CppExpressionKind.Unary,
        CXCursorKind.CXCursor_ObjCStringLiteral => CppExpressionKind.ObjCStringLiteral,
        CXCursorKind.CXCursor_ObjCEncodeExpr => CppExpressionKind.ObjCEncode,
        CXCursorKind.CXCursor_ObjCSelectorExpr => CppExpressionKind.ObjCSelector,
        CXCursorKind.CXCursor_ObjCProtocolExpr => CppExpressionKind.ObjCProtocol,
        CXCursorKind.CXCursor_ObjCBridgedCastExpr => CppExpressionKind.ObjCBridgedCast,
        CXCursorKind.CXCursor_PackExpansionExpr => CppExpressionKind.PackExpansion,
        CXCursorKind.CXCursor_SizeOfPackExpr => CppExpressionKind.SizeOfPack,
        CXCursorKind.CXCursor_LambdaExpr => CppExpressionKind.Lambda,
        CXCursorKind.CXCursor_ObjCBoolLiteralExpr => CppExpressionKind.ObjCBoolLiteral,
        CXCursorKind.CXCursor_ObjCSelfExpr => CppExpressionKind.ObjCSelf,
        CXCursorKind.CXCursor_OMPArrayShapingExpr => CppExpressionKind.OMPArrayShapingExpr,
        CXCursorKind.CXCursor_ObjCAvailabilityCheckExpr => CppExpressionKind.ObjCAvailabilityCheck,
        CXCursorKind.CXCursor_FixedPointLiteral => CppExpressionKind.FixedPointLiteral,
        _ => CppExpressionKind.Unknown,
    };
}
