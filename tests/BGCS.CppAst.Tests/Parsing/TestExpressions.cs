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
    public class TestExpressions : InlineTestBase
    {
        [Fact]
        public void TestInitListExpression()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            ParseAssert(@"
#define INITGUID
#include <guiddef.h>
DEFINE_GUID(IID_ID3D11DeviceChild,0x1841e5c8,0x16b0,0x489b,0xbc,0xc8,0x44,0xcf,0xb0,0xd5,0xde,0xae);
", compilation =>
                {
                    Assert.False(compilation.HasErrors);
                    Assert.Equal(1, compilation.Fields.Count);
                    var cppField = compilation.Fields[0];

                    Assert.Null(cppField.InitValue);

                    Assert.NotNull(cppField.InitExpression);
                    Assert.IsType<CppInitListExpression>(cppField.InitExpression);

                    var toStr = cppField.InitExpression.ToString();

                    Assert.Equal("{0x1841e5c8, 0x16b0, 0x489b, {0xbc, 0xc8, 0x44, 0xcf, 0xb0, 0xd5, 0xde, 0xae}}", toStr);
                },
                new CppParserOptions().ConfigureForWindowsMsvc());
        }


        [Fact]
        public void TestBinaryExpressions()
        {
            ParseAssert(@"
const int x = (0 + 1) << 2;
", compilation =>
            {
                Assert.False(compilation.HasErrors);
                Assert.Equal(1, compilation.Fields.Count);
                var cppField = compilation.Fields[0];

                Assert.NotNull(cppField.InitValue?.Value);
                Assert.Equal(4L, Convert.ToInt64(cppField.InitValue.Value));

                Assert.NotNull(cppField.InitExpression);
                Assert.IsType<CppBinaryExpression>(cppField.InitExpression);

                Assert.Equal("(0 + 1) << 2", cppField.InitExpression.ToString());
            });
        }

        [Fact]
        public void TestUnaryExpressions()
        {
            ParseAssert(@"
const int x = ~(128 + 2);
", compilation =>
            {
                Assert.False(compilation.HasErrors);
                Assert.Equal(1, compilation.Fields.Count);
                var cppField = compilation.Fields[0];

                Assert.NotNull(cppField.InitValue?.Value);
                var result = ~(128 + 2);
                Assert.Equal((long)result, Convert.ToInt64(cppField.InitValue.Value));

                Assert.NotNull(cppField.InitExpression);
                Assert.IsType<CppUnaryExpression>(cppField.InitExpression);

                Assert.Equal("~(128 + 2)", cppField.InitExpression.ToString());
            });
        }

        [Fact]
        public void TestBinaryOr()
        {
            ParseAssert(@"
const int x = 12|1;
", compilation =>
            {
                Assert.False(compilation.HasErrors);
                Assert.Equal(1, compilation.Fields.Count);
                var cppField = compilation.Fields[0];

                Assert.NotNull(cppField.InitValue?.Value);
                var result = 12 | 1;
                Assert.Equal((long)result, Convert.ToInt64(cppField.InitValue.Value));

                Assert.NotNull(cppField.InitExpression);
                Assert.IsType<CppBinaryExpression>(cppField.InitExpression);

                Assert.Equal("12 | 1", cppField.InitExpression.ToString());
            });
        }

        [Fact]
        public void TestParameterDefaultValue()
        {
            ParseAssert(@"
void MyFunction(int x = (1 + 2) * 3);
", compilation =>
            {
                Assert.False(compilation.HasErrors);
                Assert.Equal(1, compilation.Functions.Count);
                var parameters = compilation.Functions[0].Parameters;
                Assert.Equal(1, parameters.Count);
                var cppParam = parameters[0];

                Assert.NotNull(cppParam.InitValue?.Value);
                Assert.Equal(9L, Convert.ToInt64(cppParam.InitValue.Value));

                Assert.NotNull(cppParam.InitExpression);
                Assert.IsType<CppBinaryExpression>(cppParam.InitExpression);

                Assert.Equal("(1 + 2) * 3", cppParam.InitExpression.ToString());

                Assert.Equal("void MyFunction(int x = (1 + 2) * 3)", compilation.Functions[0].ToString());
            });
        }

        [Fact]
        public void TestNullPtrExpression()
        {
            ParseAssert(@"
const void* NullPtr = nullptr;
", compilation =>
            {
                Assert.False(compilation.HasErrors);
                Assert.Equal(1, compilation.Fields.Count);
                var cppField = compilation.Fields[0];

                Assert.Null(cppField.InitValue?.Value);

                Assert.IsType<CppRawExpression>(cppField.InitExpression);

                Assert.Equal("nullptr", cppField.InitExpression.ToString());
            });
        }
    }
}
