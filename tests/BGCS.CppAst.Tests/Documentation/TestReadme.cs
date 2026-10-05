using System;
using BGCS.CppAst.Parsing;
using Xunit;
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace BGCS.CppAst.Tests
{
    /// <summary>
    /// Code running what is being run by the readme.md
    /// </summary>
    public class TestReadme : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            // Parse a C++ files
            var compilation = CppParser.Parse(@"
enum MyEnum { MyEnum_0, MyEnum_1 };
void function0(int a, int b);
struct MyStruct { int field0; int field1;};
typedef MyStruct* MyStructPtr;
"
            );
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
        }
    }
}
