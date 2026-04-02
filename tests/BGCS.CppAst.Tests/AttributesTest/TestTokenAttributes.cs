using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;

namespace BGCS.CppAst.Tests
{
    public class TestTokenAttributes : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
__declspec(dllimport) int i;
__declspec(dllexport) void func0();
extern ""C"" void __stdcall func1(int a, int b, int c);
void *fun2(int align) __attribute__((alloc_align(1)));
",
                compilation =>
                {

                    // Print diagnostic messages
                    foreach (var message in compilation.Diagnostics.Messages)
                        Console.WriteLine(message);

                    // Print All enums
                    foreach (var cppEnum in compilation.Enums)
                        Console.WriteLine(cppEnum);

                    // Print All functions
                    foreach (var cppFunction in compilation.Functions)
                        Console.WriteLine(cppFunction);

                    // Print All classes, structs
                    foreach (var cppClass in compilation.Classes)
                        Console.WriteLine(cppClass);

                    // Print All typedefs
                    foreach (var cppTypedef in compilation.Typedefs)
                        Console.WriteLine(cppTypedef);


                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Fields.Count);
                    Assert.NotNull(compilation.Fields[0].TokenAttributes);
                    Assert.Equal("dllimport", compilation.Fields[0].TokenAttributes[0].Name);

                    Assert.Equal(3, compilation.Functions.Count);
                    Assert.NotNull(compilation.Functions[0].TokenAttributes);
                    Assert.Equal(1, compilation.Functions[0].TokenAttributes.Count);
                    Assert.Equal("dllexport", compilation.Functions[0].TokenAttributes[0].Name);

                    Assert.Equal(CppCallingConvention.X86StdCall, compilation.Functions[1].CallingConvention);

                    Assert.NotNull(compilation.Functions[2].TokenAttributes);
                    Assert.Equal(1, compilation.Functions[2].TokenAttributes.Count);
                    Assert.Equal("alloc_align(1)", compilation.Functions[2].TokenAttributes[0].ToString());

                },
                new CppParserOptions() { ParseTokenAttributes = true }.ConfigureForWindowsMsvc() // Force using X86 to get __stdcall calling convention
            );
        }

        [Fact]
        public void TestStructAttributes()
        {
            ParseAssert(@"
struct __declspec(uuid(""1841e5c8-16b0-489b-bcc8-44cfb0d5deae"")) __declspec(novtable) Test{
    int a;
    int b;
};", compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Classes.Count);

                    Assert.NotNull(compilation.Classes[0].TokenAttributes);

                    Assert.Equal(2, compilation.Classes[0].TokenAttributes.Count);

                    {
                        var attr = compilation.Classes[0].TokenAttributes[0];
                        Assert.Equal("uuid", attr.Name);
                        Assert.Equal("\"1841e5c8-16b0-489b-bcc8-44cfb0d5deae\"", attr.Arguments);
                    }

                    {
                        var attr = compilation.Classes[0].TokenAttributes[1];
                        Assert.Equal("novtable", attr.Name);
                        Assert.Null(attr.Arguments);
                    }
                },
                new CppParserOptions() { ParseTokenAttributes = true }.ConfigureForWindowsMsvc());
        }

        [Fact]
        public void TestCpp11VarAlignas()
        {
            ParseAssert(@"
alignas(128) char cacheline[128];", compilation =>
            {
                Assert.False(compilation.HasErrors);

                Assert.Equal(1, compilation.Fields.Count);
                Assert.Equal(1, compilation.Fields[0].TokenAttributes.Count);
                {
                    var attr = compilation.Fields[0].TokenAttributes[0];
                    Assert.Equal("alignas", attr.Name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { AdditionalArguments = { "-std=c++14" }, ParseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp11StructAttributes()
        {
            ParseAssert(@"
struct [[deprecated]] Test{
    int a;
    int b;
};

struct [[deprecated(""old"")]] TestMessage{
    int a;
    int b;
};", compilation =>
            {
                Assert.False(compilation.HasErrors);

                Assert.Equal(2, compilation.Classes.Count);
                Assert.Equal(1, compilation.Classes[0].TokenAttributes.Count);
                {
                    var attr = compilation.Classes[0].TokenAttributes[0];
                    Assert.Equal("deprecated", attr.Name);
                }

                Assert.Equal(1, compilation.Classes[1].TokenAttributes.Count);
                {
                    var attr = compilation.Classes[1].TokenAttributes[0];
                    Assert.Equal("deprecated", attr.Name);
                    Assert.Equal("\"old\"", attr.Arguments);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { AdditionalArguments = { "-std=c++14" }, ParseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp11StructAttributesWithMacro()
        {
            ParseAssert(@"
#define CLASS_ATTRIBUTE [[complex_attribute::attribute_name(""attribute_argument"")]]
struct
CLASS_ATTRIBUTE
Test{
    int a;
    int b;
};", compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Classes.Count);
                    Assert.Equal(1, compilation.Classes[0].TokenAttributes.Count);
                    {
                        var attr = compilation.Classes[0].TokenAttributes[0];
                        Assert.Equal("complex_attribute", attr.Scope);
                        Assert.Equal("attribute_name", attr.Name);
                        Assert.Equal("\"attribute_argument\"", attr.Arguments);
                    }
                },
                // we are using a C++14 attribute because it can be used everywhere
                new CppParserOptions() { AdditionalArguments = { "-std=c++14" }, ParseTokenAttributes = true }
            );
        }

        [Fact]
        public void TestCpp11VariablesAttributes()
        {
            ParseAssert(@"
struct Test{
    [[deprecated]] int a;
    int b;
};

[[deprecated]] int x;", compilation =>
            {
                Assert.False(compilation.HasErrors);

                Assert.Equal(1, compilation.Classes.Count);
                Assert.Equal(2, compilation.Classes[0].Fields.Count);
                Assert.Equal(1, compilation.Classes[0].Fields[0].TokenAttributes.Count);
                {
                    var attr = compilation.Classes[0].Fields[0].TokenAttributes[0];
                    Assert.Equal("deprecated", attr.Name);
                }

                Assert.Equal(1, compilation.Fields.Count);
                Assert.Equal(1, compilation.Fields[0].TokenAttributes.Count);
                {
                    var attr = compilation.Fields[0].TokenAttributes[0];
                    Assert.Equal("deprecated", attr.Name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { AdditionalArguments = { "-std=c++14" }, ParseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp11FunctionsAttributes()
        {
            ParseAssert(@"
[[noreturn]] void x() {};", compilation =>
            {
                Assert.False(compilation.HasErrors);

                Assert.Equal(1, compilation.Functions.Count);
                Assert.Equal(1, compilation.Functions[0].TokenAttributes.Count);
                {
                    var attr = compilation.Functions[0].TokenAttributes[0];
                    Assert.Equal("noreturn", attr.Name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { AdditionalArguments = { "-std=c++14" }, ParseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp11FunctionsAttributesOnNewLine()
        {
            ParseAssert(@"
[[noreturn]]
void x() {};", compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Functions.Count);
                    Assert.Equal(1, compilation.Functions[0].TokenAttributes.Count);
                    {
                        var attr = compilation.Functions[0].TokenAttributes[0];
                        Assert.Equal("noreturn", attr.Name);
                    }
                },
                // we are using a C++14 attribute because it can be used everywhere
                new CppParserOptions() { AdditionalArguments = { "-std=c++14" }, ParseTokenAttributes = true }
            );
        }

        [Fact]
        public void TestCpp11NamespaceAttributes()
        {
            ParseAssert(@"
namespace [[deprecated]] cppast {};", compilation =>
            {
                Assert.False(compilation.HasErrors);

                Assert.Equal(1, compilation.Namespaces.Count);
                Assert.Equal(1, compilation.Namespaces[0].TokenAttributes.Count);
                {
                    var attr = compilation.Namespaces[0].TokenAttributes[0];
                    Assert.Equal("deprecated", attr.Name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { AdditionalArguments = { "-std=c++14" }, ParseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp11EnumAttributes()
        {
            ParseAssert(@"
enum [[deprecated]] E { };", compilation =>
            {
                Assert.False(compilation.HasErrors);

                Assert.Equal(1, compilation.Enums.Count);
                Assert.Equal(1, compilation.Enums[0].TokenAttributes.Count);
                {
                    var attr = compilation.Enums[0].TokenAttributes[0];
                    Assert.Equal("deprecated", attr.Name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { AdditionalArguments = { "-std=c++14" }, ParseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp11TemplateStructAttributes()
        {
            ParseAssert(@"
template<typename T> struct X {};
template<> struct [[deprecated]] X<int> {};", compilation =>
            {
                Assert.False(compilation.HasErrors);

                Assert.Equal(2, compilation.Classes.Count);
                Assert.Equal(0, compilation.Classes[0].TokenAttributes.Count);
                Assert.Equal(1, compilation.Classes[1].TokenAttributes.Count);
                {
                    var attr = compilation.Classes[1].TokenAttributes[0];
                    Assert.Equal("deprecated", attr.Name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { AdditionalArguments = { "-std=c++14" }, ParseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp17StructUnknownAttributes()
        {
            ParseAssert(@"
struct [[cppast]] Test{
    int a;
    int b;
};

struct [[cppast(""old"")]] TestMessage{
    int a;
    int b;
};", compilation =>
            {
                Assert.False(compilation.HasErrors);

                Assert.Equal(2, compilation.Classes.Count);
                Assert.Equal(1, compilation.Classes[0].TokenAttributes.Count);
                {
                    var attr = compilation.Classes[0].TokenAttributes[0];
                    Assert.Equal("cppast", attr.Name);
                }

                Assert.Equal(1, compilation.Classes[1].TokenAttributes.Count);
                {
                    var attr = compilation.Classes[1].TokenAttributes[0];
                    Assert.Equal("cppast", attr.Name);
                    Assert.Equal("\"old\"", attr.Arguments);
                }
            },
            // C++17 says if the compile encounters a attribute it doesn't understand
            // it will ignore that attribute and not throw an error, we still want to
            // parse this.
            new CppParserOptions() { AdditionalArguments = { "-std=c++17" }, ParseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCommentParen()
        {
            ParseAssert(@"
// [infinite loop)
int function1(int a, int b);
", compilation =>
            {
                Assert.False(compilation.HasErrors);

                var expectedText = @"[infinite loop)";

                Assert.Equal(1, compilation.Functions.Count);
                var resultText = compilation.Functions[0].Comment?.ToString();

                expectedText = expectedText.Replace("\r\n", "\n");
                resultText = resultText?.Replace("\r\n", "\n");
                Assert.Equal(expectedText, resultText);

                Assert.Equal(0, compilation.Functions[0].TokenAttributes.Count);
            },
            new CppParserOptions() { ParseTokenAttributes = true });
        }

        [Fact]
        public void TestCommentParenWithAttribute()
        {
            ParseAssert(@"
// [infinite loop)
[[noreturn]] int function1(int a, int b);
", compilation =>
            {
                Assert.False(compilation.HasErrors);

                var expectedText = @"[infinite loop)";

                Assert.Equal(1, compilation.Functions.Count);
                var resultText = compilation.Functions[0].Comment?.ToString();

                expectedText = expectedText.Replace("\r\n", "\n");
                resultText = resultText?.Replace("\r\n", "\n");
                Assert.Equal(expectedText, resultText);

                Assert.Equal(1, compilation.Functions[0].TokenAttributes.Count);
            },
            new CppParserOptions() { ParseTokenAttributes = true });
        }

        [Fact]
        public void TestCommentWithAttributeCharacters()
        {
            ParseAssert(@"
// (infinite loop)
// [[infinite loop]]
// bug(infinite loop)
int function1(int a, int b);", compilation =>
            {
                Assert.False(compilation.HasErrors);

                var expectedText = @"(infinite loop)
[[infinite loop]]
bug(infinite loop)";

                Assert.Equal(1, compilation.Functions.Count);
                var resultText = compilation.Functions[0].Comment?.ToString();

                expectedText = expectedText.Replace("\r\n", "\n");
                resultText = resultText?.Replace("\r\n", "\n");
                Assert.Equal(expectedText, resultText);

                Assert.Equal(0, compilation.Functions[0].TokenAttributes.Count);
            },
            new CppParserOptions() { ParseTokenAttributes = true });
        }

        [Fact]
        public void TestAttributeInvalidBracketEnd()
        {
            ParseAssert(@"
// noreturn]]
int function1(int a, int b);", compilation =>
            {
                Assert.False(compilation.HasErrors);
                Assert.Equal(0, compilation.Functions[0].TokenAttributes.Count);
            },
            new CppParserOptions() { ParseTokenAttributes = true });
        }

        [Fact]
        public void TestAttributeInvalidParenEnd()
        {
            ParseAssert(@"
// noreturn)
int function1(int a, int b);", compilation =>
            {
                Assert.False(compilation.HasErrors);
                Assert.Equal(0, compilation.Functions[0].TokenAttributes.Count);
            },
            new CppParserOptions() { ParseTokenAttributes = true });
        }

        [Fact]
        public void TestCpp17VarTemplateAttribute()
        {
            ParseAssert(@"
template<typename T>
struct TestT {
};

struct Test{
    [[cppast]] TestT<int> channels;
};", compilation =>
            {
                Assert.False(compilation.HasErrors);

                Assert.Equal(3, compilation.Classes.Count);
                Assert.Equal(1, compilation.Classes[1].Fields.Count);
                Assert.Equal(1, compilation.Classes[1].Fields[0].TokenAttributes.Count);
                {
                    var attr = compilation.Classes[1].Fields[0].TokenAttributes[0];
                    Assert.Equal("cppast", attr.Name);
                }
            },
            // C++17 says if the compile encounters a attribute it doesn't understand
            // it will ignore that attribute and not throw an error, we still want to
            // parse this.
            new CppParserOptions() { AdditionalArguments = { "-std=c++17" }, ParseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp17FunctionTemplateAttribute()
        {
            ParseAssert(@"
struct Test{
    template<typename W> [[cppast]] W GetFoo();
};", compilation =>
            {
                Assert.False(compilation.HasErrors);

                Assert.Equal(1, compilation.Classes.Count);
                Assert.Equal(1, compilation.Classes[0].Functions.Count);
                Assert.Equal(1, compilation.Classes[0].Functions[0].TokenAttributes.Count);
                {
                    var attr = compilation.Classes[0].Functions[0].TokenAttributes[0];
                    Assert.Equal("cppast", attr.Name);
                }
            },
            // C++17 says if the compile encounters a attribute it doesn't understand
            // it will ignore that attribute and not throw an error, we still want to
            // parse this.
            new CppParserOptions() { AdditionalArguments = { "-std=c++17" }, ParseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCppNoParseOptionsAttributes()
        {
            ParseAssert(@"
[[noreturn]] void x() {};", compilation =>
            {
                Assert.False(compilation.HasErrors);

                Assert.Equal(1, compilation.Functions.Count);
                Assert.Equal(0, compilation.Functions[0].TokenAttributes.Count);
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { AdditionalArguments = { "-std=c++14" }, ParseTokenAttributes = false }
          );
        }


    }
}
