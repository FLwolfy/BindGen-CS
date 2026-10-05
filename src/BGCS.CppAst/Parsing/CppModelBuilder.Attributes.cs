namespace BGCS.CppAst.Parsing;

using System.Collections.Generic;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>CppModelBuilder</c>.
/// </summary>
internal unsafe partial class CppModelBuilder
{
    private static List<CppAttribute> ParseSystemAndAnnotateAttributeInCursor(CXCursor cursor)
    {
        List<CppAttribute> attributes = [];
        using DGCHandle<List<CppAttribute>> handle = new(attributes);
        cursor.VisitChildren(static (
            argCursor,
            parentCursor,
            clientData
        ) => {
            List<CppAttribute> attributes = DGCHandle<List<CppAttribute>>.ObjFrom(clientData);
            var sourceSpan = argCursor.GetSourceRange();
            var meta = CXUtil.GetCursorSpelling(argCursor);
            switch (argCursor.Kind)
            {
                case CXCursorKind.CXCursor_VisibilityAttr:
                    {
                        CppAttribute attribute = new(argCursor, "visibility", AttributeKind.CxxSystemAttribute);
                        attribute.AssignSourceSpan(argCursor);
                        attribute.arguments = string.Format("\"{0}\"", CXUtil.GetCursorDisplayName(argCursor));
                        attributes.Add(attribute);
                    }

                    break;
                case CXCursorKind.CXCursor_AnnotateAttr:
                    {
                        CppAttribute attribute = new(argCursor, "annotate", AttributeKind.AnnotateAttribute)
                        {
                            span = sourceSpan,
                            arguments = meta,
                        };
                        attributes.Add(attribute);
                    }

                    break;
                case CXCursorKind.CXCursor_AlignedAttr:
                    {
                        var attrKindSpelling = argCursor.AttrKindSpelling.ToLower();
                        CppAttribute attribute = new(argCursor, "alignas", AttributeKind.CxxSystemAttribute)
                        {
                            span = sourceSpan,
                        };
                        attributes.Add(attribute);
                    }

                    break;
                case CXCursorKind.CXCursor_UnexposedAttr:
                    {
                        var attrKind = argCursor.AttrKind;
                        var attrKindSpelling = argCursor.AttrKindSpelling.ToLower();
                        CppAttribute attribute = new(argCursor, attrKindSpelling, AttributeKind.CxxSystemAttribute)
                        {
                            span = sourceSpan,
                        };
                        attributes.Add(attribute);
                    }

                    break;
                case CXCursorKind.CXCursor_DLLImport:
                case CXCursorKind.CXCursor_DLLExport:
                    {
                        var attrKind = argCursor.AttrKind;
                        var attrKindSpelling = argCursor.AttrKindSpelling.ToLower();
                        CppAttribute attribute = new(argCursor, attrKindSpelling, AttributeKind.CxxSystemAttribute)
                        {
                            span = sourceSpan,
                        };
                        attributes.Add(attribute);
                    }

                    break;
                // Don't generate a warning for unsupported cursor
                default:
                    break;
            }

            return CXChildVisitResult.CXChildVisit_Continue;
        }, handle);
        return attributes;
    }

    /// <summary>
    /// Executes public operation <c>ParseAttributes</c>.
    /// </summary>
    public void ParseAttributes(
        CXCursor cursor,
        ICppAttributeContainer attrContainer,
        bool needOnlineSeek = false
    ) {
        //Try to handle annotate in cursor first
        //Low spend handle here, just open always
        attrContainer.attributes.AddRange(ParseSystemAndAnnotateAttributeInCursor(cursor));
        // Low performance tokens handle here
        if (!this.parseTokenAttributeEnabled)
            return;
        var globalDeclarationContainer = this.m_context.globalDeclarationContainer;
        List<CppAttribute> attributes = [];
        // Parse attributes online
        if (needOnlineSeek)
        {
            bool hasOnlineAttribute = CppTokenUtil.TryToSeekOnlineAttributes(cursor, out var onLineRange);
            if (hasOnlineAttribute)
            {
                CppTokenUtil.ParseAttributesInRange(globalDeclarationContainer, cursor.TranslationUnit, onLineRange, ref attributes);
            }
        }

        // Parse attributes contains in cursor
        if (attrContainer is CppFunction func)
        {
            CppTokenUtil.ParseFunctionAttributes(globalDeclarationContainer, cursor, func.name, ref attributes);
        }
        else
        {
            CppTokenUtil.ParseCursorAttributes(globalDeclarationContainer, cursor, ref attributes);
        }

        // The preceding source range and the cursor extent can both contain the same
        // leading attribute. Keep distinct source occurrences, not duplicate scans.
        HashSet<(string File, int Start, int End, string Name, AttributeKind Kind)> seen = [];
        foreach (CppAttribute attribute in attributes)
        {
            var key = (attribute.span.start.file, attribute.span.start.offset, attribute.span.end.offset, attribute.name, attribute.kind);
            if (seen.Add(key))
                attrContainer.tokenAttributes.Add(attribute);
        }
    }

    /// <summary>
    /// Executes public operation <c>ParseTypedefAttribute</c>.
    /// </summary>
    public void ParseTypedefAttribute(
        CXCursor cursor,
        CppType type,
        CppType underlyingTypeDefType
    ) {
        if (type is CppTypedef typedef)
        {
            ParseAttributes(cursor, typedef, true);
            if (underlyingTypeDefType is CppClass targetClass)
            {
                targetClass.attributes.AddRange(typedef.attributes);
                targetClass.ConvertToMetaAttributes();
            }
        }
    }
}
