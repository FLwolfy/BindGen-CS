using System;
using BGCS.Core.Targeting;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

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
                    Assert.False(compilation.hasErrors);
                    Assert.Single(compilation.fields);
                    var cppField = compilation.fields[0];

                    Assert.Null(cppField.initValue);

                    Assert.NotNull(cppField.initExpression);
                    Assert.IsType<CppInitListExpression>(cppField.initExpression);

                    var toStr = cppField.initExpression.ToString();

                    Assert.Equal("{0x1841e5c8, 0x16b0, 0x489b, {0xbc, 0xc8, 0x44, 0xcf, 0xb0, 0xd5, 0xde, 0xae}}", toStr);
                },
                new CppParserOptions().ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId("windows-x86-msvc")))));
        }


        [Fact]
        public void TestBinaryExpressions()
        {
            ParseAssert(@"
const int x = (0 + 1) << 2;
", compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Single(compilation.fields);
                var cppField = compilation.fields[0];

                Assert.NotNull(cppField.initValue?.value);
                Assert.Equal(4L, Convert.ToInt64(cppField.initValue.value));

                Assert.NotNull(cppField.initExpression);
                Assert.IsType<CppBinaryExpression>(cppField.initExpression);

                Assert.Equal("(0 + 1) << 2", cppField.initExpression.ToString());
            });
        }

        [Fact]
        public void TestUnaryExpressions()
        {
            ParseAssert(@"
const int x = ~(128 + 2);
", compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Single(compilation.fields);
                var cppField = compilation.fields[0];

                Assert.NotNull(cppField.initValue?.value);
                var result = ~(128 + 2);
                Assert.Equal((long)result, Convert.ToInt64(cppField.initValue.value));

                Assert.NotNull(cppField.initExpression);
                Assert.IsType<CppUnaryExpression>(cppField.initExpression);

                Assert.Equal("~(128 + 2)", cppField.initExpression.ToString());
            });
        }

        [Fact]
        public void TestBinaryOr()
        {
            ParseAssert(@"
const int x = 12|1;
", compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Single(compilation.fields);
                var cppField = compilation.fields[0];

                Assert.NotNull(cppField.initValue?.value);
                var result = 12 | 1;
                Assert.Equal((long)result, Convert.ToInt64(cppField.initValue.value));

                Assert.NotNull(cppField.initExpression);
                Assert.IsType<CppBinaryExpression>(cppField.initExpression);

                Assert.Equal("12 | 1", cppField.initExpression.ToString());
            });
        }

        [Fact]
        public void TestParameterDefaultValue()
        {
            ParseAssert(@"
void MyFunction(int x = (1 + 2) * 3);
", compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Single(compilation.functions);
                var parameters = compilation.functions[0].parameters;
                Assert.Single(parameters);
                var cppParam = parameters[0];

                Assert.NotNull(cppParam.initValue?.value);
                Assert.Equal(9L, Convert.ToInt64(cppParam.initValue.value));

                Assert.NotNull(cppParam.initExpression);
                Assert.IsType<CppBinaryExpression>(cppParam.initExpression);

                Assert.Equal("(1 + 2) * 3", cppParam.initExpression.ToString());

                Assert.Equal("void MyFunction(int x = (1 + 2) * 3)", compilation.functions[0].ToString());
            });
        }

        [Fact]
        public void TestNullPtrExpression()
        {
            ParseAssert(@"
const void* NullPtr = nullptr;
", compilation =>
            {
                Assert.False(compilation.hasErrors);
                Assert.Single(compilation.fields);
                var cppField = compilation.fields[0];

                Assert.Null(cppField.initValue?.value);

                Assert.IsType<CppRawExpression>(cppField.initExpression);

                Assert.Equal("nullptr", cppField.initExpression.ToString());
            });
        }
    }
}
