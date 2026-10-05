using System;
using System.Linq;
using BGCS.Core.Targeting;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Tests;

public class TestObjectiveC : InlineTestBase
{
    [Fact]
    public void TestPlatformSystemIncludesSmoke()
    {
        ParseAssert("""
                    #include <stdint.h>
                    #include <stddef.h>
                    #include <stdbool.h>
                    """,
            compilation =>
            {
                var errors = compilation.diagnostics.messages
                    .Where(x => x.type == BGCS.CppAst.Diagnostics.CppLogMessageType.Error)
                    .ToList();

                if (errors.Count == 0)
                {
                    return;
                }

                // Some CI environments intentionally don't provide SDK/header toolchains.
                // Treat "file not found" diagnostics as an environment skip.
                if (errors.All(IsMissingSystemHeaderError))
                {
                    return;
                }

                Assert.Fail(string.Join(Environment.NewLine, errors.Select(x => x.ToString())));
            }, CreateCurrentPlatformSystemIncludeOptions()
        );
    }

    [Fact]
    public void TestInterfaceWithMethods()
    {
        ParseAssert("""
                    @interface MyInterface
                        - (float)helloworld;
                        - (void)doSomething:(int)index argSpecial:(float)arg1;
                    @end
                    """,
            compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Single(compilation.classes);
                var myInterface = compilation.classes[0];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.classKind);
                Assert.Equal("MyInterface", myInterface.name);
                Assert.Equal(2, myInterface.functions.Count);

                Assert.Empty(myInterface.functions[0].parameters);
                Assert.Equal("helloworld", myInterface.functions[0].name);
                Assert.True(myInterface.functions[0].returnType is CppPrimitiveType primitive && primitive.kind == CppPrimitiveKind.Float);

                Assert.Equal(2, myInterface.functions[1].parameters.Count);
                Assert.Equal("index", myInterface.functions[1].parameters[0].name);
                Assert.Equal("arg1", myInterface.functions[1].parameters[1].name);
                Assert.Equal("doSomething:argSpecial:", myInterface.functions[1].name);
                Assert.True(myInterface.functions[1].returnType is CppPrimitiveType primitive2 && primitive2.kind == CppPrimitiveKind.Void);
                Assert.True(myInterface.functions[1].parameters[1].type is CppPrimitiveType primitive3 && primitive3.kind == CppPrimitiveKind.Float);
            }, GetDefaultObjCOptions()
        );
    }

    [Fact]
    public void TestInterfaceWithProperties()
    {
        ParseAssert("""
                    @interface MyInterface
                        @property int id;
                        @property (readonly) float id2;
                    @end
                    """,
            compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Single(compilation.classes);
                var myInterface = compilation.classes[0];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.classKind);
                Assert.Equal("MyInterface", myInterface.name);
                Assert.Equal(2, myInterface.properties.Count);
                Assert.Equal("id", myInterface.properties[0].name);
                Assert.Equal("id2", myInterface.properties[1].name);

                Assert.True(myInterface.properties[0].type is CppPrimitiveType primitive && primitive.kind == CppPrimitiveKind.Int);
                Assert.True(myInterface.properties[0].getter is not null);
                Assert.True(myInterface.properties[0].setter is not null);

                Assert.True(myInterface.properties[1].type is CppPrimitiveType primitive2 && primitive2.kind == CppPrimitiveKind.Float);
                Assert.True(myInterface.properties[1].setter is null);

                Assert.Equal(3, myInterface.functions.Count);
                Assert.Equal("id", myInterface.functions[0].name);
                Assert.True(myInterface.functions[0].returnType is CppPrimitiveType primitive3 && primitive3.kind == CppPrimitiveKind.Int);

                Assert.Equal("setId:", myInterface.functions[1].name);
                Assert.True(myInterface.functions[1].returnType is CppPrimitiveType primitive4 && primitive4.kind == CppPrimitiveKind.Void);
                Assert.Single(myInterface.functions[1].parameters);
                Assert.Equal("id", myInterface.functions[1].parameters[0].name);
                Assert.True(myInterface.functions[1].parameters[0].type is CppPrimitiveType primitive5 && primitive5.kind == CppPrimitiveKind.Int);

                Assert.Equal("id2", myInterface.functions[2].name);
                Assert.True(myInterface.functions[2].returnType is CppPrimitiveType primitive6 && primitive6.kind == CppPrimitiveKind.Float);
                Assert.Empty(myInterface.functions[2].parameters);
            }, GetDefaultObjCOptions()
        );
    }

    [Fact]
    public void TestInterfaceWithInstanceType()
    {
        ParseAssert("""
                    @interface MyInterface
                        + (instancetype)getInstance;
                    @end
                    """,
            compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Single(compilation.classes);
                var myInterface = compilation.classes[0];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.classKind);
                Assert.Equal("MyInterface", myInterface.name);
                Assert.Single(myInterface.functions);
                Assert.Equal("getInstance", myInterface.functions[0].name);
                Assert.True((myInterface.functions[0].flags & CppFunctionFlags.ClassMethod) != 0);
                var pointerType = myInterface.functions[0].returnType as CppPointerType;
                Assert.NotNull(pointerType);
                Assert.Equal(myInterface, pointerType!.elementType);
            }, GetDefaultObjCOptions()
        );
    }

    [Fact]
    public void TestInterfaceWithMultipleGenericParameters()
    {
        ParseAssert("""
                    @interface BaseInterface
                    @end

                    // Generics require a base class
                    @interface MyInterface<T1, T2> : BaseInterface
                        - (T1)get_at:(int)index;
                        - (T2)get_at2:(int)index;
                    @end
                    """,
            compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Equal(2, compilation.classes.Count);
                var myInterface = compilation.classes[1];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.classKind);
                Assert.Equal("MyInterface", myInterface.name);
                Assert.Equal(2, myInterface.templateParameters.Count);
                Assert.True(myInterface.templateParameters[0] is CppTemplateParameterType templateParam1 && templateParam1.name == "T1");
                Assert.True(myInterface.templateParameters[1] is CppTemplateParameterType templateParam2 && templateParam2.name == "T2");

                Assert.Equal(2, myInterface.functions.Count);
                Assert.Equal("get_at:", myInterface.functions[0].name);
                Assert.Equal("get_at2:", myInterface.functions[1].name);
                Assert.True(myInterface.functions[0].returnType is CppTemplateParameterType templateSpecialization && templateSpecialization.name == "T1");
                Assert.True(myInterface.functions[1].returnType is CppTemplateParameterType templateSpecialization2 && templateSpecialization2.name == "T2");
            }, GetDefaultObjCOptions()
        );
    }

    [Fact]
    public void TestInterfaceWithGenericsAndTypedef()
    {
        ParseAssert("""
                    @interface BaseInterface
                    @end

                    // Generics require a base class
                    @interface MyInterface<T1> : BaseInterface
                        typedef T1 HelloWorld;
                    @end
                    """,
            compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Equal(2, compilation.classes.Count);
                var myInterface = compilation.classes[1];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.classKind);
                Assert.Equal("MyInterface", myInterface.name);
                Assert.Single(myInterface.templateParameters);
                Assert.True(myInterface.templateParameters[0] is CppTemplateParameterType templateParam1 && templateParam1.name == "T1");

                var text = myInterface.ToString();
                Assert.Equal("@interface MyInterface<T1> : BaseInterface", text);

                // By default, typedef declared within interfaces are global, but in that case, it is depending on a template parameter
                // So it is not part of the global namespace
                Assert.Empty(compilation.typedefs);
                Assert.Single(myInterface.typedefs);
                var typedef = myInterface.typedefs[0];
                Assert.Equal("HelloWorld", typedef.name);
                Assert.True(typedef.elementType is CppTemplateParameterType templateSpecialization && templateSpecialization.name == "T1");
            }, GetDefaultObjCOptions()
        );
    }

    [Fact]
    public void TestBlockFunctionPointer()
    {
        ParseAssert("""
                    typedef float (^MyBlock)(int a, int* b);
                    """,
            compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Single(compilation.typedefs);

                var typedef = compilation.typedefs[0];
                Assert.Equal("MyBlock", typedef.name);

                Assert.IsType<CppBlockFunctionType>(typedef.elementType);
                var blockType = (CppBlockFunctionType)typedef.elementType;

                Assert.Equal(CppTypeKind.ObjCBlockFunction, blockType.typeKind);

                Assert.True(blockType.returnType is CppPrimitiveType primitive && primitive.kind == CppPrimitiveKind.Float);

                Assert.Equal(2, blockType.parameters.Count);
                Assert.True(blockType.parameters[0].type is CppPrimitiveType primitive2 && primitive2.kind == CppPrimitiveKind.Int);
                Assert.True(blockType.parameters[1].type is CppPointerType pointerType && pointerType.elementType is CppPrimitiveType primitive3 && primitive3.kind == CppPrimitiveKind.Int);
            }, GetDefaultObjCOptions()
        );
    }

    [Fact]
    public void TestProtocol()
    {
        ParseAssert("""
                    @protocol MyProtocol
                    @end

                    @protocol MyProtocol1
                    @end

                    @protocol MyProtocol2 <MyProtocol, MyProtocol1>
                    @end

                    @interface MyInterface <MyProtocol>
                    @end
                    """,
            compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Equal(4, compilation.classes.Count);

                var myProtocol = compilation.classes[0];
                Assert.Equal(CppClassKind.ObjCProtocol, myProtocol.classKind);
                Assert.Equal("MyProtocol", myProtocol.name);

                var myProtocol1 = compilation.classes[1];
                Assert.Equal(CppClassKind.ObjCProtocol, myProtocol1.classKind);
                Assert.Equal("MyProtocol1", myProtocol1.name);

                var myProtocol2 = compilation.classes[2];
                Assert.Equal(CppClassKind.ObjCProtocol, myProtocol2.classKind);
                Assert.Equal("MyProtocol2", myProtocol2.name);
                Assert.Equal(2, myProtocol2.objCImplementedProtocols.Count);
                Assert.Equal(myProtocol, myProtocol2.objCImplementedProtocols[0]);
                Assert.Equal(myProtocol1, myProtocol2.objCImplementedProtocols[1]);

                var text2 = myProtocol2.ToString();
                Assert.Equal("@protocol MyProtocol2 <MyProtocol, MyProtocol1>", text2);

                var myInterface = compilation.classes[3];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.classKind);
                Assert.Equal("MyInterface", myInterface.name);
                Assert.Single(myInterface.objCImplementedProtocols);
                Assert.Equal(myProtocol, myInterface.objCImplementedProtocols[0]);
            }, GetDefaultObjCOptions()
        );
    }

    [Fact]
    public void TestInterfaceBaseType()
    {
        ParseAssert("""
                    @interface InterfaceBase
                    @end

                    @interface MyInterface : InterfaceBase
                    @end
                    """,
            compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Equal(2, compilation.classes.Count);

                var myInterfaceBase = compilation.classes[0];
                Assert.Equal(CppClassKind.ObjCInterface, myInterfaceBase.classKind);
                Assert.Empty(myInterfaceBase.baseTypes);
                Assert.Equal("InterfaceBase", myInterfaceBase.name);

                var myInterface = compilation.classes[1];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.classKind);
                Assert.Equal("MyInterface", myInterface.name);
                Assert.Single(myInterface.baseTypes);
                Assert.Equal(myInterfaceBase, myInterface.baseTypes[0].type);
            }, GetDefaultObjCOptions()
        );
    }

    [Fact]
    public void TestInterfaceWithCategory()
    {
        ParseAssert("""
                    @interface MyInterface
                    @end

                    @interface MyInterface (MyCategory)
                    @end
                    """,
            compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Equal(2, compilation.classes.Count);

                var myInterface = compilation.classes[0];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.classKind);
                Assert.Equal("MyInterface", myInterface.name);

                var myInterfaceWithCategory = compilation.classes[1];
                Assert.Equal(CppClassKind.ObjCInterfaceCategory, myInterfaceWithCategory.classKind);
                Assert.Equal("MyInterface", myInterfaceWithCategory.name);
                Assert.Equal("MyCategory", myInterfaceWithCategory.objCCategoryName);
                Assert.Equal(myInterface, myInterfaceWithCategory.objCCategoryTargetClass);

                var text = myInterfaceWithCategory.ToString();
                Assert.Equal("@interface MyInterface (MyCategory)", text);
            }, GetDefaultObjCOptions()
        );
    }

    private static CppParserOptions GetDefaultObjCOptions()
    {
        return new CppParserOptions
        {
            parserKind = CppParserKind.ObjC,
            targetTriple = "arm64-apple-darwin",
            parseMacros = false,
            parseSystemIncludes = false,
        };
    }

    private static CppParserOptions CreateCurrentPlatformSystemIncludeOptions()
    {
        var options = new CppParserOptions
        {
            parserKind = CppParserKind.C,
            parseMacros = false,
            parseComments = false,
            parseSystemIncludes = true
        };

        options.ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId("host"))));
        return options;
    }

    private static bool IsMissingSystemHeaderError(BGCS.CppAst.Diagnostics.CppDiagnosticMessage message)
    {
        var text = message.text;
        return text.Contains("file not found", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("cannot open source file", StringComparison.OrdinalIgnoreCase);
    }
}
