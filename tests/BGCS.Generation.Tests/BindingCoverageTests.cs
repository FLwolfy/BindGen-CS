using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Core.Logging;
using BGCS.CppAst.Parsing;
using Xunit;

namespace BGCS.Tests;

public class BindingCoverageTests
{
    public static IEnumerable<object[]> CAndCppBindingCases()
    {
        yield return
        [
            "C struct + enum + functions",
            """
            typedef enum MyEnum
            {
                MY_ENUM_A = 0,
                MY_ENUM_B = 1
            } MyEnum;

            typedef struct MyStruct
            {
                int a;
                float b;
            } MyStruct;

            void Test_UseEnum(MyEnum value);
            void Test_CreateStruct(MyStruct* value);
            """,
            new[] { "EntryPoint = \"Test_UseEnum\"", "EntryPoint = \"Test_CreateStruct\"", "TestUseEnum", "TestCreateStruct" }
        ];

        yield return
        [
            "C callback typedef",
            """
            typedef void (*MyCallback)(int value);
            void Test_SetCallback(MyCallback callback);
            """,
            new[] { "EntryPoint = \"Test_SetCallback\"", "MyCallback" }
        ];

        yield return
        [
            "C macros to constants",
            """
            #define MY_CONST_A 42
            #define MY_CONST_B 0x10
            void Test_NoOp(void);
            """,
            new[] { "EntryPoint = \"Test_NoOp\"" }
        ];

        yield return
        [
            "C typedef alias and pointer params",
            """
            typedef unsigned int MyUInt;
            MyUInt Test_GetValue(void);
            void Test_SetValue(MyUInt value);
            """,
            new[] { "EntryPoint = \"Test_GetValue\"", "EntryPoint = \"Test_SetValue\"", "TestGetValue", "TestSetValue" }
        ];

        yield return
        [
            "C++ namespace + class + extern C function",
            """
            namespace Demo
            {
                class CppPod
                {
                public:
                    int value;
                };
            }

            extern "C" int Test_Add(int a, int b);
            """,
            new[] { "EntryPoint = \"Test_Add\"", "TestAdd" }
        ];
    }

    [Theory]
    [MemberData(nameof(CAndCppBindingCases))]
    public void Generate_CAndCppBindings_ShouldProduceExpectedFragments(string caseName, string header, string[] expectedFragments)
    {
        var result = RunGenerator(header);
        bool passed = false;
        try
        {
            AssertGeneratorSucceeded(result.Ok, result.Messages);

            foreach (string fragment in expectedFragments)
            {
                Assert.True(
                    result.AllGeneratedCode.Contains(fragment, StringComparison.Ordinal),
                    $"Case '{caseName}' expected fragment not found: {fragment}\nHeader:\n{header}\nTempDir: {result.TempDirectory}\nGenerated:\n{result.AllGeneratedCode}");
            }

            if (caseName == "C callback typedef")
            {
                bool hasTypedFunctionPointer = result.AllGeneratedCode.Contains("delegate*<int, void>", StringComparison.Ordinal);
                Assert.True(
                    hasTypedFunctionPointer,
                    $"Case '{caseName}' expected function pointer signature not found.\nHeader:\n{header}\nTempDir: {result.TempDirectory}\nGenerated:\n{result.AllGeneratedCode}");
            }

            passed = true;
        }
        finally
        {
            if (passed)
            {
                Cleanup(result.TempDirectory);
            }
        }
    }

    private static void AssertGeneratorSucceeded(bool ok, IReadOnlyList<LogMessage> messages)
    {
        Assert.True(ok);
        Assert.DoesNotContain(messages, x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);
    }

    private static (bool Ok, string TempDirectory, string AllGeneratedCode, IReadOnlyList<LogMessage> Messages) RunGenerator(string headerText)
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string headerPath = Path.Combine(temp, "input.h");
        string outputPath = Path.Combine(temp, "out");
        File.WriteAllText(headerPath, headerText);

        CsCodeGeneratorConfig cfg = new()
        {
            ApiName = "BindingApi",
            Namespace = "BGCS.Tests.Generated",
            LibName = "bindingtest",
            GenerateExtensions = false,
            ImportType = ImportType.DllImport,
            DelegatesAsVoidPointer = false
        };

        BaseGenerator generator = new CsCodeGenerator(cfg);
        CppParserOptions parserOptions = new()
        {
            ParseMacros = true,
            ParseComments = true,
            ParseSystemIncludes = false,
            ParseCommentAttribute = true,
            ParserKind = CppParserKind.Cpp,
            AutoSquashTypedef = false
        };
        parserOptions.AdditionalArguments.Add("-undef");

        bool ok = ((CsCodeGenerator)generator).Generate(parserOptions, headerPath, outputPath);

        string allGeneratedCode = string.Empty;
        if (Directory.Exists(outputPath))
        {
            var files = Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories);
            allGeneratedCode = string.Join("\n\n", files.Select(File.ReadAllText));
        }

        return (ok, temp, allGeneratedCode, generator.Messages);
    }

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
