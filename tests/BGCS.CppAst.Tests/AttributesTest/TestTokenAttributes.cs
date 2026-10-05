using System;
using BGCS.Core.Targeting;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

#pragma warning disable CS0618 // This suite verifies the compatibility surface of the obsolete token-attribute API.

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
                    foreach (var message in compilation.diagnostics.messages)
                        Console.WriteLine(message);

                    // Print All enums
                    foreach (var cppEnum in compilation.enums)
                        Console.WriteLine(cppEnum);

                    // Print All functions
                    foreach (var cppFunction in compilation.functions)
                        Console.WriteLine(cppFunction);

                    // Print All classes, structs
                    foreach (var cppClass in compilation.classes)
                        Console.WriteLine(cppClass);

                    // Print All typedefs
                    foreach (var cppTypedef in compilation.typedefs)
                        Console.WriteLine(cppTypedef);


                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.fields);
                    Assert.NotNull(compilation.fields[0].tokenAttributes);
                    Assert.Equal("dllimport", compilation.fields[0].tokenAttributes[0].name);

                    Assert.Equal(3, compilation.functions.Count);
                    Assert.NotNull(compilation.functions[0].tokenAttributes);
                    Assert.Single(compilation.functions[0].tokenAttributes);
                    Assert.Equal("dllexport", compilation.functions[0].tokenAttributes[0].name);

                    Assert.Equal(CppCallingConvention.X86StdCall, compilation.functions[1].callingConvention);

                    Assert.NotNull(compilation.functions[2].tokenAttributes);
                    Assert.Single(compilation.functions[2].tokenAttributes);
                    Assert.Equal("alloc_align(1)", compilation.functions[2].tokenAttributes[0].ToString());

                },
                new CppParserOptions() { parseTokenAttributes = true }.ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId("windows-x86-msvc")))) // Force using X86 to get __stdcall calling convention
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
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.classes);

                    Assert.NotNull(compilation.classes[0].tokenAttributes);

                    Assert.Equal(2, compilation.classes[0].tokenAttributes.Count);

                    {
                        var attr = compilation.classes[0].tokenAttributes[0];
                        Assert.Equal("uuid", attr.name);
                        Assert.Equal("\"1841e5c8-16b0-489b-bcc8-44cfb0d5deae\"", attr.arguments);
                    }

                    {
                        var attr = compilation.classes[0].tokenAttributes[1];
                        Assert.Equal("novtable", attr.name);
                        Assert.Null(attr.arguments);
                    }
                },
                new CppParserOptions() { parseTokenAttributes = true }.ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId("windows-x86-msvc")))));
        }

        [Fact]
        public void TestCpp11VarAlignas()
        {
            ParseAssert(@"
alignas(128) char cacheline[128];", compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Single(compilation.fields);
                Assert.Single(compilation.fields[0].tokenAttributes);
                {
                    var attr = compilation.fields[0].tokenAttributes[0];
                    Assert.Equal("alignas", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" }, parseTokenAttributes = true }
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
                Assert.False(compilation.hasErrors);

                Assert.Equal(2, compilation.classes.Count);
                Assert.Single(compilation.classes[0].tokenAttributes);
                {
                    var attr = compilation.classes[0].tokenAttributes[0];
                    Assert.Equal("deprecated", attr.name);
                }

                Assert.Single(compilation.classes[1].tokenAttributes);
                {
                    var attr = compilation.classes[1].tokenAttributes[0];
                    Assert.Equal("deprecated", attr.name);
                    Assert.Equal("\"old\"", attr.arguments);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" }, parseTokenAttributes = true }
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
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.classes);
                    Assert.Single(compilation.classes[0].tokenAttributes);
                    {
                        var attr = compilation.classes[0].tokenAttributes[0];
                        Assert.Equal("complex_attribute", attr.scope);
                        Assert.Equal("attribute_name", attr.name);
                        Assert.Equal("\"attribute_argument\"", attr.arguments);
                    }
                },
                // we are using a C++14 attribute because it can be used everywhere
                new CppParserOptions() { additionalArguments = { "-std=c++14" }, parseTokenAttributes = true }
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
                Assert.False(compilation.hasErrors);

                Assert.Single(compilation.classes);
                Assert.Equal(2, compilation.classes[0].fields.Count);
                Assert.Single(compilation.classes[0].fields[0].tokenAttributes);
                {
                    var attr = compilation.classes[0].fields[0].tokenAttributes[0];
                    Assert.Equal("deprecated", attr.name);
                }

                Assert.Single(compilation.fields);
                Assert.Single(compilation.fields[0].tokenAttributes);
                {
                    var attr = compilation.fields[0].tokenAttributes[0];
                    Assert.Equal("deprecated", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" }, parseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp11FunctionsAttributes()
        {
            ParseAssert(@"
[[noreturn]] void x() {};", compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Single(compilation.functions);
                Assert.Single(compilation.functions[0].tokenAttributes);
                {
                    var attr = compilation.functions[0].tokenAttributes[0];
                    Assert.Equal("noreturn", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" }, parseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp11FunctionsAttributesOnNewLine()
        {
            ParseAssert(@"
[[noreturn]]
void x() {};", compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.functions);
                    Assert.Single(compilation.functions[0].tokenAttributes);
                    {
                        var attr = compilation.functions[0].tokenAttributes[0];
                        Assert.Equal("noreturn", attr.name);
                    }
                },
                // we are using a C++14 attribute because it can be used everywhere
                new CppParserOptions() { additionalArguments = { "-std=c++14" }, parseTokenAttributes = true }
            );
        }

        [Fact]
        public void FunctionAttributes_UseDeclaratorLocationWhenReturnTypeCallsSameName()
        {
            ParseAssert(@"
int factory();
[[nodiscard]] decltype(factory()) factory(int value) __attribute__((annotate(""tail"")));
", compilation =>
            {
                Assert.False(compilation.hasErrors);
                CppFunction overload = Assert.Single(compilation.functions, function => function.parameters.Count == 1);
                Assert.Contains(overload.tokenAttributes, attribute => attribute.name == "nodiscard");
                Assert.Contains(overload.tokenAttributes, attribute => attribute.name == "annotate");
            }, new CppParserOptions { additionalArguments = { "-std=c++17" }, parseTokenAttributes = true });
        }

        [Fact]
        public void TypedefAttributes_BeforeCursorExtentAreParsed()
        {
            ParseAssert(@"
[[deprecated]] typedef int LegacyNumber;
", compilation =>
            {
                Assert.False(compilation.hasErrors);
                CppTypedef alias = Assert.Single(compilation.typedefs);
                Assert.Contains(alias.tokenAttributes, attribute => attribute.name == "deprecated");
            }, new CppParserOptions { additionalArguments = { "-std=c++17" }, parseTokenAttributes = true });
        }

        [Fact]
        public void TestCpp11NamespaceAttributes()
        {
            ParseAssert(@"
namespace [[deprecated]] cppast {};", compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Single(compilation.namespaces);
                Assert.Single(compilation.namespaces[0].tokenAttributes);
                {
                    var attr = compilation.namespaces[0].tokenAttributes[0];
                    Assert.Equal("deprecated", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" }, parseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp11EnumAttributes()
        {
            ParseAssert(@"
enum [[deprecated]] E { };", compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Single(compilation.enums);
                Assert.Single(compilation.enums[0].tokenAttributes);
                {
                    var attr = compilation.enums[0].tokenAttributes[0];
                    Assert.Equal("deprecated", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" }, parseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCpp11TemplateStructAttributes()
        {
            ParseAssert(@"
template<typename T> struct X {};
template<> struct [[deprecated]] X<int> {};", compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Equal(2, compilation.classes.Count);
                Assert.Empty(compilation.classes[0].tokenAttributes);
                Assert.Single(compilation.classes[1].tokenAttributes);
                {
                    var attr = compilation.classes[1].tokenAttributes[0];
                    Assert.Equal("deprecated", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" }, parseTokenAttributes = true }
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
                Assert.False(compilation.hasErrors);

                Assert.Equal(2, compilation.classes.Count);
                Assert.Single(compilation.classes[0].tokenAttributes);
                {
                    var attr = compilation.classes[0].tokenAttributes[0];
                    Assert.Equal("cppast", attr.name);
                }

                Assert.Single(compilation.classes[1].tokenAttributes);
                {
                    var attr = compilation.classes[1].tokenAttributes[0];
                    Assert.Equal("cppast", attr.name);
                    Assert.Equal("\"old\"", attr.arguments);
                }
            },
            // C++17 says if the compile encounters a attribute it doesn't understand
            // it will ignore that attribute and not throw an error, we still want to
            // parse this.
            new CppParserOptions() { additionalArguments = { "-std=c++17" }, parseTokenAttributes = true }
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
                Assert.False(compilation.hasErrors);

                var expectedText = @"[infinite loop)";

                Assert.Single(compilation.functions);
                var resultText = compilation.functions[0].comment?.ToString();

                expectedText = expectedText.Replace("\r\n", "\n");
                resultText = resultText?.Replace("\r\n", "\n");
                Assert.Equal(expectedText, resultText);

                Assert.Empty(compilation.functions[0].tokenAttributes);
            },
            new CppParserOptions() { parseTokenAttributes = true });
        }

        [Fact]
        public void TestCommentParenWithAttribute()
        {
            ParseAssert(@"
// [infinite loop)
[[noreturn]] int function1(int a, int b);
", compilation =>
            {
                Assert.False(compilation.hasErrors);

                var expectedText = @"[infinite loop)";

                Assert.Single(compilation.functions);
                var resultText = compilation.functions[0].comment?.ToString();

                expectedText = expectedText.Replace("\r\n", "\n");
                resultText = resultText?.Replace("\r\n", "\n");
                Assert.Equal(expectedText, resultText);

                Assert.Single(compilation.functions[0].tokenAttributes);
            },
            new CppParserOptions() { parseTokenAttributes = true });
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
                Assert.False(compilation.hasErrors);

                var expectedText = @"(infinite loop)
[[infinite loop]]
bug(infinite loop)";

                Assert.Single(compilation.functions);
                var resultText = compilation.functions[0].comment?.ToString();

                expectedText = expectedText.Replace("\r\n", "\n");
                resultText = resultText?.Replace("\r\n", "\n");
                Assert.Equal(expectedText, resultText);

                Assert.Empty(compilation.functions[0].tokenAttributes);
            },
            new CppParserOptions() { parseTokenAttributes = true });
        }

        [Fact]
        public void TestAttributeInvalidBracketEnd()
        {
            ParseAssert(@"
// noreturn]]
int function1(int a, int b);", compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Empty(compilation.functions[0].tokenAttributes);
            },
            new CppParserOptions() { parseTokenAttributes = true });
        }

        [Fact]
        public void TestAttributeInvalidParenEnd()
        {
            ParseAssert(@"
// noreturn)
int function1(int a, int b);", compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Empty(compilation.functions[0].tokenAttributes);
            },
            new CppParserOptions() { parseTokenAttributes = true });
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
                Assert.False(compilation.hasErrors);

                Assert.Equal(3, compilation.classes.Count);
                Assert.Single(compilation.classes[1].fields);
                Assert.Single(compilation.classes[1].fields[0].tokenAttributes);
                {
                    var attr = compilation.classes[1].fields[0].tokenAttributes[0];
                    Assert.Equal("cppast", attr.name);
                }
            },
            // C++17 says if the compile encounters a attribute it doesn't understand
            // it will ignore that attribute and not throw an error, we still want to
            // parse this.
            new CppParserOptions() { additionalArguments = { "-std=c++17" }, parseTokenAttributes = true }
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
                Assert.False(compilation.hasErrors);

                Assert.Single(compilation.classes);
                Assert.Single(compilation.classes[0].functions);
                Assert.Single(compilation.classes[0].functions[0].tokenAttributes);
                {
                    var attr = compilation.classes[0].functions[0].tokenAttributes[0];
                    Assert.Equal("cppast", attr.name);
                }
            },
            // C++17 says if the compile encounters a attribute it doesn't understand
            // it will ignore that attribute and not throw an error, we still want to
            // parse this.
            new CppParserOptions() { additionalArguments = { "-std=c++17" }, parseTokenAttributes = true }
          );
        }

        [Fact]
        public void TestCppNoParseOptionsAttributes()
        {
            ParseAssert(@"
[[noreturn]] void x() {};", compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Single(compilation.functions);
                Assert.Empty(compilation.functions[0].tokenAttributes);
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" }, parseTokenAttributes = false }
          );
        }


    }
}
