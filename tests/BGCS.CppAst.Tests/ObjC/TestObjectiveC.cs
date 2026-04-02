using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Extensions;
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
                var errors = compilation.Diagnostics.Messages
                    .Where(x => x.Type == BGCS.CppAst.Diagnostics.CppLogMessageType.Error)
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
                Assert.False(compilation.HasErrors);
                Assert.Equal(1, compilation.Classes.Count);
                var myInterface = compilation.Classes[0];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.ClassKind);
                Assert.Equal("MyInterface", myInterface.Name);
                Assert.Equal(2, myInterface.Functions.Count);

                Assert.Equal(0, myInterface.Functions[0].Parameters.Count);
                Assert.Equal("helloworld", myInterface.Functions[0].Name);
                Assert.True(myInterface.Functions[0].ReturnType is CppPrimitiveType primitive && primitive.Kind == CppPrimitiveKind.Float);

                Assert.Equal(2, myInterface.Functions[1].Parameters.Count);
                Assert.Equal("index", myInterface.Functions[1].Parameters[0].Name);
                Assert.Equal("arg1", myInterface.Functions[1].Parameters[1].Name);
                Assert.Equal("doSomething:argSpecial:", myInterface.Functions[1].Name);
                Assert.True(myInterface.Functions[1].ReturnType is CppPrimitiveType primitive2 && primitive2.Kind == CppPrimitiveKind.Void);
                Assert.True(myInterface.Functions[1].Parameters[1].Type is CppPrimitiveType primitive3 && primitive3.Kind == CppPrimitiveKind.Float);
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
                Assert.False(compilation.HasErrors);
                Assert.Equal(1, compilation.Classes.Count);
                var myInterface = compilation.Classes[0];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.ClassKind);
                Assert.Equal("MyInterface", myInterface.Name);
                Assert.Equal(2, myInterface.Properties.Count);
                Assert.Equal("id", myInterface.Properties[0].Name);
                Assert.Equal("id2", myInterface.Properties[1].Name);

                Assert.True(myInterface.Properties[0].Type is CppPrimitiveType primitive && primitive.Kind == CppPrimitiveKind.Int);
                Assert.True(myInterface.Properties[0].Getter is not null);
                Assert.True(myInterface.Properties[0].Setter is not null);

                Assert.True(myInterface.Properties[1].Type is CppPrimitiveType primitive2 && primitive2.Kind == CppPrimitiveKind.Float);
                Assert.True(myInterface.Properties[1].Setter is null);

                Assert.Equal(3, myInterface.Functions.Count);
                Assert.Equal("id", myInterface.Functions[0].Name);
                Assert.True(myInterface.Functions[0].ReturnType is CppPrimitiveType primitive3 && primitive3.Kind == CppPrimitiveKind.Int);

                Assert.Equal("setId:", myInterface.Functions[1].Name);
                Assert.True(myInterface.Functions[1].ReturnType is CppPrimitiveType primitive4 && primitive4.Kind == CppPrimitiveKind.Void);
                Assert.Equal(1, myInterface.Functions[1].Parameters.Count);
                Assert.Equal("id", myInterface.Functions[1].Parameters[0].Name);
                Assert.True(myInterface.Functions[1].Parameters[0].Type is CppPrimitiveType primitive5 && primitive5.Kind == CppPrimitiveKind.Int);

                Assert.Equal("id2", myInterface.Functions[2].Name);
                Assert.True(myInterface.Functions[2].ReturnType is CppPrimitiveType primitive6 && primitive6.Kind == CppPrimitiveKind.Float);
                Assert.Equal(0, myInterface.Functions[2].Parameters.Count);
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
                Assert.False(compilation.HasErrors);
                Assert.Equal(1, compilation.Classes.Count);
                var myInterface = compilation.Classes[0];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.ClassKind);
                Assert.Equal("MyInterface", myInterface.Name);
                Assert.Equal(1, myInterface.Functions.Count);
                Assert.Equal("getInstance", myInterface.Functions[0].Name);
                Assert.True((myInterface.Functions[0].Flags & CppFunctionFlags.ClassMethod) != 0);
                var pointerType = myInterface.Functions[0].ReturnType as CppPointerType;
                Assert.NotNull(pointerType);
                Assert.Equal(myInterface, pointerType!.ElementType);
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
                Assert.False(compilation.HasErrors);
                Assert.Equal(2, compilation.Classes.Count);
                var myInterface = compilation.Classes[1];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.ClassKind);
                Assert.Equal("MyInterface", myInterface.Name);
                Assert.Equal(2, myInterface.TemplateParameters.Count);
                Assert.True(myInterface.TemplateParameters[0] is CppTemplateParameterType templateParam1 && templateParam1.Name == "T1");
                Assert.True(myInterface.TemplateParameters[1] is CppTemplateParameterType templateParam2 && templateParam2.Name == "T2");

                Assert.Equal(2, myInterface.Functions.Count);
                Assert.Equal("get_at:", myInterface.Functions[0].Name);
                Assert.Equal("get_at2:", myInterface.Functions[1].Name);
                Assert.True(myInterface.Functions[0].ReturnType is CppTemplateParameterType templateSpecialization && templateSpecialization.Name == "T1");
                Assert.True(myInterface.Functions[1].ReturnType is CppTemplateParameterType templateSpecialization2 && templateSpecialization2.Name == "T2");
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
                Assert.False(compilation.HasErrors);
                Assert.Equal(2, compilation.Classes.Count);
                var myInterface = compilation.Classes[1];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.ClassKind);
                Assert.Equal("MyInterface", myInterface.Name);
                Assert.Equal(1, myInterface.TemplateParameters.Count);
                Assert.True(myInterface.TemplateParameters[0] is CppTemplateParameterType templateParam1 && templateParam1.Name == "T1");

                var text = myInterface.ToString();
                Assert.Equal("@interface MyInterface<T1> : BaseInterface", text);

                // By default, typedef declared within interfaces are global, but in that case, it is depending on a template parameter
                // So it is not part of the global namespace
                Assert.Equal(0, compilation.Typedefs.Count);
                Assert.Equal(1, myInterface.Typedefs.Count);
                var typedef = myInterface.Typedefs[0];
                Assert.Equal("HelloWorld", typedef.Name);
                Assert.True(typedef.ElementType is CppTemplateParameterType templateSpecialization && templateSpecialization.Name == "T1");
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
                Assert.False(compilation.HasErrors);
                Assert.Equal(1, compilation.Typedefs.Count);

                var typedef = compilation.Typedefs[0];
                Assert.Equal("MyBlock", typedef.Name);

                Assert.IsType<CppBlockFunctionType>(typedef.ElementType);
                var blockType = (CppBlockFunctionType)typedef.ElementType;

                Assert.Equal(CppTypeKind.ObjCBlockFunction, blockType.TypeKind);

                Assert.True(blockType.ReturnType is CppPrimitiveType primitive && primitive.Kind == CppPrimitiveKind.Float);

                Assert.Equal(2, blockType.Parameters.Count);
                Assert.True(blockType.Parameters[0].Type is CppPrimitiveType primitive2 && primitive2.Kind == CppPrimitiveKind.Int);
                Assert.True(blockType.Parameters[1].Type is CppPointerType pointerType && pointerType.ElementType is CppPrimitiveType primitive3 && primitive3.Kind == CppPrimitiveKind.Int);
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
                Assert.False(compilation.HasErrors);

                Assert.Equal(4, compilation.Classes.Count);

                var myProtocol = compilation.Classes[0];
                Assert.Equal(CppClassKind.ObjCProtocol, myProtocol.ClassKind);
                Assert.Equal("MyProtocol", myProtocol.Name);

                var myProtocol1 = compilation.Classes[1];
                Assert.Equal(CppClassKind.ObjCProtocol, myProtocol1.ClassKind);
                Assert.Equal("MyProtocol1", myProtocol1.Name);

                var myProtocol2 = compilation.Classes[2];
                Assert.Equal(CppClassKind.ObjCProtocol, myProtocol2.ClassKind);
                Assert.Equal("MyProtocol2", myProtocol2.Name);
                Assert.Equal(2, myProtocol2.ObjCImplementedProtocols.Count);
                Assert.Equal(myProtocol, myProtocol2.ObjCImplementedProtocols[0]);
                Assert.Equal(myProtocol1, myProtocol2.ObjCImplementedProtocols[1]);

                var text2 = myProtocol2.ToString();
                Assert.Equal("@protocol MyProtocol2 <MyProtocol, MyProtocol1>", text2);

                var myInterface = compilation.Classes[3];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.ClassKind);
                Assert.Equal("MyInterface", myInterface.Name);
                Assert.Equal(1, myInterface.ObjCImplementedProtocols.Count);
                Assert.Equal(myProtocol, myInterface.ObjCImplementedProtocols[0]);
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
                Assert.False(compilation.HasErrors);

                Assert.Equal(2, compilation.Classes.Count);

                var myInterfaceBase = compilation.Classes[0];
                Assert.Equal(CppClassKind.ObjCInterface, myInterfaceBase.ClassKind);
                Assert.Equal(0, myInterfaceBase.BaseTypes.Count);
                Assert.Equal("InterfaceBase", myInterfaceBase.Name);

                var myInterface = compilation.Classes[1];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.ClassKind);
                Assert.Equal("MyInterface", myInterface.Name);
                Assert.Equal(1, myInterface.BaseTypes.Count);
                Assert.Equal(myInterfaceBase, myInterface.BaseTypes[0].Type);
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
                Assert.False(compilation.HasErrors);

                Assert.Equal(2, compilation.Classes.Count);

                var myInterface = compilation.Classes[0];
                Assert.Equal(CppClassKind.ObjCInterface, myInterface.ClassKind);
                Assert.Equal("MyInterface", myInterface.Name);

                var myInterfaceWithCategory = compilation.Classes[1];
                Assert.Equal(CppClassKind.ObjCInterfaceCategory, myInterfaceWithCategory.ClassKind);
                Assert.Equal("MyInterface", myInterfaceWithCategory.Name);
                Assert.Equal("MyCategory", myInterfaceWithCategory.ObjCCategoryName);
                Assert.Equal(myInterface, myInterfaceWithCategory.ObjCCategoryTargetClass);

                var text = myInterfaceWithCategory.ToString();
                Assert.Equal("@interface MyInterface (MyCategory)", text);
            }, GetDefaultObjCOptions()
        );
    }

    private static CppParserOptions GetDefaultObjCOptions()
    {
        return new CppParserOptions
        {
            ParserKind = CppParserKind.ObjC,
            TargetCpu = CppTargetCpu.ARM64,
            TargetVendor = "apple",
            TargetSystem = "darwin",
            ParseMacros = false,
            ParseSystemIncludes = false,
        };
    }

    private static CppParserOptions CreateCurrentPlatformSystemIncludeOptions()
    {
        var options = new CppParserOptions
        {
            ParserKind = CppParserKind.C,
            ParseMacros = false,
            ParseComments = false,
            ParseSystemIncludes = true
        };

        if (OperatingSystem.IsWindows())
        {
            var targetCpu = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => CppTargetCpu.ARM64,
                Architecture.Arm => CppTargetCpu.ARM,
                Architecture.X64 => CppTargetCpu.X86_64,
                _ => CppTargetCpu.X86
            };
            options.ConfigureForWindowsMsvc(targetCpu);
            return options;
        }

        options.TargetCpu = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => CppTargetCpu.ARM64,
            Architecture.Arm => CppTargetCpu.ARM,
            Architecture.X64 => CppTargetCpu.X86_64,
            _ => CppTargetCpu.X86
        };
        options.TargetCpuSub = string.Empty;
        options.TargetVendor = OperatingSystem.IsMacOS() ? "apple" : "pc";
        options.TargetSystem = OperatingSystem.IsMacOS() ? "darwin" : "linux";
        options.TargetAbi = string.Empty;
        return options;
    }

    private static bool IsMissingSystemHeaderError(BGCS.CppAst.Diagnostics.CppDiagnosticMessage message)
    {
        var text = message.Text;
        return text.Contains("file not found", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("cannot open source file", StringComparison.OrdinalIgnoreCase);
    }
}
