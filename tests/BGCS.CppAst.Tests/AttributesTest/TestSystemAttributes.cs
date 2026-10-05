using System;
using BGCS.Core.Targeting;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace BGCS.CppAst.Tests
{
    public class TestSystemAttributes : InlineTestBase
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
                    Assert.NotNull(compilation.fields[0].attributes);
                    Assert.Equal("dllimport", compilation.fields[0].attributes[0].name);

                    Assert.Equal(3, compilation.functions.Count);
                    Assert.NotNull(compilation.functions[0].attributes);
                    Assert.Single(compilation.functions[0].attributes);
                    Assert.Equal("dllexport", compilation.functions[0].attributes[0].name);

                    Assert.Equal(CppCallingConvention.X86StdCall, compilation.functions[1].callingConvention);

                    Assert.NotNull(compilation.functions[2].attributes);
                    Assert.Single(compilation.functions[2].attributes);
                    Assert.Equal("allocalign", compilation.functions[2].attributes[0].name);

                },
                new CppParserOptions() { }.ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId("windows-x86-msvc")))) // Force using X86 to get __stdcall calling convention
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

                    Assert.NotNull(compilation.classes[0].attributes);

                    Assert.Equal(2, compilation.classes[0].attributes.Count);

                    {
                        var attr = compilation.classes[0].attributes[0];
                        Assert.Equal("uuid", attr.name);
                    }

                    {
                        var attr = compilation.classes[0].attributes[1];
                        Assert.Equal("msnovtable", attr.name);
                    }
                },
                new CppParserOptions() { }.ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId("windows-x86-msvc")))));
        }

        [Fact]
        public void TestCpp11VarAlignas()
        {
            ParseAssert(@"
alignas(128) char cacheline[128];", compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Single(compilation.fields);
                Assert.Single(compilation.fields[0].attributes);
                {
                    var attr = compilation.fields[0].attributes[0];
                    Assert.Equal("alignas", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" } }
          );
        }

        [Fact]
        public void TestCpp11StructAlignas()
        {
            ParseAssert(@"
struct alignas(8) S {};", compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Single(compilation.classes);
                Assert.Single(compilation.classes[0].attributes);
                {
                    var attr = compilation.classes[0].attributes[0];
                    Assert.Equal("alignas", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" } }
          );
        }

        [Fact]
        public void TestCpp11StructAlignasWithAttribute()
        {
            ParseAssert(@"
struct [[deprecated(""abc"")]] alignas(8) S {};", compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Single(compilation.classes);
                Assert.Equal(2, compilation.classes[0].attributes.Count);
                {
                    var attr = compilation.classes[0].attributes[0];
                    Assert.Equal("deprecated", attr.name);
                }

                {
                    var attr = compilation.classes[0].attributes[1];
                    Assert.Equal("alignas", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" } }
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
                Assert.Single(compilation.classes[0].attributes);
                {
                    var attr = compilation.classes[0].attributes[0];
                    Assert.Equal("deprecated", attr.name);
                }

                Assert.Single(compilation.classes[1].attributes);
                {
                    var attr = compilation.classes[1].attributes[0];
                    Assert.Equal("deprecated", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" } }
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
                Assert.Single(compilation.classes[0].fields[0].attributes);
                {
                    var attr = compilation.classes[0].fields[0].attributes[0];
                    Assert.Equal("deprecated", attr.name);
                }

                Assert.Single(compilation.fields);
                Assert.Single(compilation.fields[0].attributes);
                {
                    var attr = compilation.fields[0].attributes[0];
                    Assert.Equal("deprecated", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" } }
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
                Assert.Single(compilation.functions[0].attributes);
                {
                    var attr = compilation.functions[0].attributes[0];
                    Assert.Equal("cxx11noreturn", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" } }
          );
        }

        [Fact]
        public void TestCpp11NamespaceAttributes()
        {
            ParseAssert(@"
namespace [[deprecated]] cppast {};", compilation =>
            {
                Assert.False(compilation.hasErrors);

                Assert.Single(compilation.namespaces);
                Assert.Single(compilation.namespaces[0].attributes);
                {
                    var attr = compilation.namespaces[0].attributes[0];
                    Assert.Equal("deprecated", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" } }
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
                Assert.Single(compilation.enums[0].attributes);
                {
                    var attr = compilation.enums[0].attributes[0];
                    Assert.Equal("deprecated", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" } }
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
                Assert.Empty(compilation.classes[0].attributes);
                Assert.Single(compilation.classes[1].attributes);
                {
                    var attr = compilation.classes[1].attributes[0];
                    Assert.Equal("deprecated", attr.name);
                }
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" } }
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
                Assert.Single(compilation.functions[0].attributes);
            },
            // we are using a C++14 attribute because it can be used everywhere
            new CppParserOptions() { additionalArguments = { "-std=c++14" } }
          );
        }

        [Fact]
        public void TestClassPublicExportAttribute()
        {
            var text = @"
#ifdef WIN32
#define EXPORT_API __declspec(dllexport)
#else
#define EXPORT_API __attribute__((visibility(""default"")))
#endif
class EXPORT_API TestClass
{
};
";
            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    var cppClass = compilation.classes[0];
                    Assert.Single(cppClass.attributes);
                    Assert.True(cppClass.IsPublicExport());

                },
                new CppParserOptions() { }
            );
            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    var cppClass = compilation.classes[0];
                    Assert.Single(cppClass.attributes);
                    Assert.True(cppClass.IsPublicExport());
                }, new CppParserOptions() { }.ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId("windows-x86-msvc"))))
            );
        }

    }
}
