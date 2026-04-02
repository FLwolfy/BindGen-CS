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
        }
    }
}