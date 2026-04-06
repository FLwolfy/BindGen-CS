using Xunit;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;
using System;
using System.IO;
using System.Linq;

namespace BGCS.Tests;

public class FormatHelperTests
{
    [Fact]
    public void NormalizeConstantValue_MultiSegmentWideString_ShouldFlattenToVerbatim()
    {
        string value = "L\"foo\"R\"bar\"";

        string normalized = value.NormalizeConstantValue();

        Assert.Equal("@\"foobar\"", normalized);
    }

    [Fact]
    public void NormalizeConstantValue_LRRawString_ShouldPreserveNewlines()
    {
        string value = "LR\"line1\nline2\"";

        string normalized = value.NormalizeConstantValue();

        Assert.Equal("@\"line1\nline2\"", normalized);
    }

    [Fact]
    public void IsCaps_ShouldReturnFalseWhenAnyLowercasePresent()
    {
        Assert.True("GL_TEXTURE_2D".IsCaps());
        Assert.False("GlTexture".IsCaps());
    }

    [Fact]
    public void PointerHelpers_ShouldDetectDepthAndElementType()
    {
        CppType intType = CppPrimitiveType.Int;
        CppType ptr = new CppPointerType(default, intType);
        CppType ptrPtr = new CppPointerType(default, ptr);

        int depth = 0;
        bool isPointer = ptrPtr.IsPointer(ref depth, out CppType pointerType);

        Assert.True(isPointer);
        Assert.Equal(2, depth);
        Assert.Same(intType, pointerType);
        Assert.True(intType.IsPointerOf(ptr));
        depth = 0;
        Assert.True(intType.IsPointerOf(ptrPtr, ref depth));
        Assert.Equal(2, depth);
    }

    [Fact]
    public void PrimitiveHelpers_ShouldResolveThroughTypedefAndPointer()
    {
        CppTypedef alias = new(default, "AliasInt", CppPrimitiveType.Int);
        CppPointerType pointer = new(default, alias);

        Assert.True(pointer.IsPrimitive(out var primitive));
        Assert.Equal(CppPrimitiveKind.Int, primitive!.Kind);
        Assert.True(CppPrimitiveType.Void.IsVoid());
        Assert.False(CppPrimitiveType.Int.IsVoid());
    }

    [Theory]
    [InlineData("(~0U)", "~0u")]
    [InlineData("(~0ULL)", "~0ul")]
    [InlineData("(~0U-2)", "~0u - 2")]
    [InlineData("123ULL", "123UL")]
    public void NormalizeEnumValue_ShouldApplyExpectedRewrites(string input, string expected)
    {
        Assert.Equal(expected, input.NormalizeEnumValue());
    }

    [Fact]
    public void NormalizeConstantValue_WideAndRawStrings_ShouldNormalizeToCSharpLiteral()
    {
        Assert.Equal("\"abc\"", "L\"abc\"".NormalizeConstantValue());
        Assert.Equal("@\"abc\"", "R\"abc\"".NormalizeConstantValue());
        Assert.Equal("@\"a\nb\"", "LR\"a\nb\"".NormalizeConstantValue());
    }

    [Theory]
    [InlineData("1+2*3", true)]
    [InlineData("A+B", true)]
    [InlineData("A_B+1", false)]
    [InlineData("1<<2", true)]
    [InlineData("1|2", false)]
    public void IsConstantExpression_ShouldClassifySupportedCharacterSet(string expr, bool expected)
    {
        Assert.Equal(expected, expr.IsConstantExpression());
    }

    [Theory]
    [InlineData("\"hello\"", true)]
    [InlineData("hello", false)]
    public void IsString_StringOverload_ShouldMatchQuotedLiterals(string value, bool expected)
    {
        Assert.Equal(expected, value.IsString());
    }

    [Fact]
    public void IsString_TypeOverload_ShouldDetectCharPointerAndMappedTypedef()
    {
        CsCodeGeneratorConfig cfg = new();

        CppType charPtr = new CppPointerType(default, CppPrimitiveType.Char);
        Assert.True(charPtr.IsString(cfg, out var charKind));
        Assert.Equal(CppPrimitiveKind.Char, charKind);

        CppTypedef typedef = new(default, "Utf8String", CppPrimitiveType.Int);
        cfg.TypeMappings["Utf8String"] = "byte*";
        Assert.True(typedef.IsString(cfg, out var mappedKind));
        Assert.Equal(CppPrimitiveKind.Char, mappedKind);
    }

    [Fact]
    public void TemplateParameterHelpers_ShouldDetectAndRenderGenericPointerSignature()
    {
        CppUnexposedType templateParam = new(default, "T");
        CppFunction function = new(default, "Foo");
        function.TemplateParameters.Add(templateParam);

        CppType paramType = new CppPointerType(default, new CppQualifiedType(default, CppTypeQualifier.Const, templateParam));

        Assert.True(paramType.IsTemplateParameter(function));
        Assert.Equal("T*", paramType.GetTemplateParameterCsName("T"));
    }

    [Fact]
    public void GetPrimitiveKind_ShouldReportPointerUnderlyingPrimitive()
    {
        CppType array = new CppArrayType(default, new CppPointerType(default, CppPrimitiveType.WChar), 3);

        CppPrimitiveKind kind = array.GetPrimitiveKind();

        Assert.Equal(CppPrimitiveKind.WChar, kind);
    }

    [Fact]
    public void IsUsedAsPointer_ShouldCollectPointerDepthsFromFunctions()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-format-pointer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "types.h");
        File.WriteAllText(header,
            """
            typedef struct Obj Obj;
            Obj* create_obj(void);
            void use_obj(Obj** value);
            """);

        try
        {
            CppParserOptions options = new() { ParseMacros = false, ParseSystemIncludes = false, ParserKind = CppParserKind.C };
            CppCompilation compilation = CppParser.ParseFile(header, options);
            CppClass objClass = Assert.Single(compilation.Classes.Where(c => c.Name == "Obj"));

            bool usedAsPointer = objClass.IsUsedAsPointer(compilation, out var depths);

            Assert.True(usedAsPointer);
            Assert.Contains(1, depths);
            Assert.Contains(2, depths);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
