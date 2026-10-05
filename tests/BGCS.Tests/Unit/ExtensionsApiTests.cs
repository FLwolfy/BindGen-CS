using System;
using System.IO;
using System.Runtime.InteropServices;
using BGCS.Analysis;
using BGCS.Conversion;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CSharp;
using BGCS.Text;
using Xunit;

namespace BGCS.Tests;

public class ExtensionsApiTests
{
    [Theory]
    [InlineData(CppCallingConvention.C, CallingConvention.Cdecl, "Cdecl", "System.Runtime.CompilerServices.CallConvCdecl")]
    [InlineData(CppCallingConvention.X86StdCall, CallingConvention.StdCall, "Stdcall", "System.Runtime.CompilerServices.CallConvStdcall")]
    public void CallingConventionHelpers_ShouldMapExpectedValues(CppCallingConvention input, CallingConvention expectedInterop, string expectedDelegate, string expectedLibrary)
    {
        Assert.Equal(expectedInterop, input.GetCallingConvention());
        Assert.Equal(expectedDelegate, input.GetCallingConventionDelegate());
        Assert.Equal(expectedLibrary, input.GetCallingConventionLibrary());
    }

    [Fact]
    public void GetDirection_ShouldHandlePrimitivePointerReferenceAndConstPointer()
    {
        Direction primitiveDir = CppPrimitiveType.@int.GetDirection();
        Direction pointerDir = new CppPointerType(default, CppPrimitiveType.@int, System.IntPtr.Size).GetDirection();
        Direction referenceDir = new CppReferenceType(default, CppPrimitiveType.@int).GetDirection();
        Direction constPointerDir = new CppPointerType(default, new CppQualifiedType(default, CppTypeQualifier.Const, CppPrimitiveType.@int), System.IntPtr.Size).GetDirection();

        Assert.Equal(Direction.In, primitiveDir);
        Assert.Equal(Direction.InOut, pointerDir);
        Assert.Equal(Direction.Out, referenceDir);
        Assert.Equal(Direction.In, constPointerDir);
    }

    [Fact]
    public void CanBeUsedAsOutput_ShouldSupportTypedefAndSizedStruct()
    {
        CppTypedef typedef = new(default, "MyInt", CppPrimitiveType.@int);
        CppType typedefPointer = new CppPointerType(default, typedef, System.IntPtr.Size);

        CppClass structType = new(default, "MyStruct") { classKind = CppClassKind.Struct, sizeOf = 4 };
        CppType structPointer = new CppPointerType(default, structType, System.IntPtr.Size);

        Assert.True(typedefPointer.CanBeUsedAsOutput(out var typedefResult));
        Assert.Same(typedef, typedefResult);
        Assert.True(structPointer.CanBeUsedAsOutput(out var structResult));
        Assert.Same(structType, structResult);
    }

    [Fact]
    public void CanonicalRootAndTypeQueries_ShouldResolveThroughWrappers()
    {
        CppEnum enumType = new(default, "Mode") { integerType = CppPrimitiveType.@int };
        CppTypedef enumAlias = new(default, "ModeAlias", enumType);
        CppPointerType pointerToAlias = new(default, enumAlias, IntPtr.Size);

        CppType canonicalWithTypedef = pointerToAlias.GetCanonicalRoot(followTypedefs: true);
        CppType canonicalWithoutTypedef = pointerToAlias.GetCanonicalRoot(followTypedefs: false);

        Assert.Same(enumType, canonicalWithTypedef);
        Assert.IsType<CppTypedef>(canonicalWithoutTypedef);
        Assert.True(pointerToAlias.IsEnum(out var outEnum));
        Assert.Same(enumType, outEnum);
    }

    [Fact]
    public void ClassAndDelegateQueries_ShouldResolveThroughPointers()
    {
        CppClass classType = new(default, "Widget");
        CppType wrappedClass = new CppPointerType(default, new CppQualifiedType(default, CppTypeQualifier.Const, classType), System.IntPtr.Size);

        Assert.True(wrappedClass.IsClass(out var resolvedClass));
        Assert.Same(classType, resolvedClass);

        CppFunctionType callbackType = new(default, CppPrimitiveType.@void);
        CppPointerType callbackPointer = new(default, callbackType, IntPtr.Size);
        Assert.True(callbackPointer.IsDelegate(out var resolvedDelegate));
        Assert.Same(callbackType, resolvedDelegate);
        Assert.True(((CppType)callbackPointer).IsDelegate());
    }

    [Fact]
    public void OpaqueHandleDetection_ShouldRequirePointerToNonDefinitionClass()
    {
        CppClass incomplete = new(default, "NativeHandle") { isDefinition = false };
        CppTypedef handle = new(default, "Handle", new CppPointerType(default, incomplete, System.IntPtr.Size));

        CppClass complete = new(default, "NativeHandleDef") { isDefinition = true };
        CppTypedef notHandle = new(default, "NotHandle", new CppPointerType(default, complete, System.IntPtr.Size));

        Assert.True(handle.IsOpaqueHandle());
        Assert.False(notHandle.IsOpaqueHandle());
    }

    [Fact]
    public void StringHelpers_ShouldConvertCaseAndSplitByCase()
    {
        Assert.Equal("Api2Version", "API2VERSION".ToTitleCaseFragment());
        Assert.Equal(["XML", "Http", "2", "Request"], "XMLHttp2Request".SplitByCase());
    }

    [Fact]
    public void ComObjectHeuristic_ShouldDetectAbstractMethodOnlyClass()
    {
        CppClass cppClass = new(default, "IComLike") { isAbstract = true };
        cppClass.functions.Add(new CppFunction(default, "DoWork"));

        Assert.True(cppClass.IsCOMObject());
    }

    [Fact]
    public void MacroHelpers_ShouldFindMacroByName()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-ext-macro-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "macros.h");
        File.WriteAllText(header, "#define BGCS_ALPHA 1\n#define BGCS_BETA 2");

        try
        {
            CppParserOptions options = new() { parseMacros = true, parseSystemIncludes = false, parserKind = CppParserKind.C };
            CppCompilation compilation = CppParser.ParseFile(header, options);

            CppMacro? alpha = compilation.FindMacro("BGCS_ALPHA");
            bool foundBeta = compilation.TryFindMacro("BGCS_BETA", out var beta);
            bool foundNone = compilation.TryFindMacro("BGCS_GAMMA", out _);

            Assert.NotNull(alpha);
            Assert.True(foundBeta);
            Assert.NotNull(beta);
            Assert.False(foundNone);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    [Fact]
    public void FormatLocationAttribute_ShouldEmbedSourceSpanData()
    {
        CppMacro macro = new(default, "TEST")
        {
            span = new CppSourceSpan(
                new CppSourceLocation("demo.h", 0, 10, 2),
                new CppSourceLocation("demo.h", 5, 10, 7))
        };

        string attr = macro.FormatLocationAttribute();

        Assert.Contains("SourceLocation", attr);
        Assert.Contains("demo.h", attr);
        Assert.Contains("(10, 2)", attr);
    }
}
