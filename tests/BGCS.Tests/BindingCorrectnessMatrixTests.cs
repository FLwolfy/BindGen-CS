using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Core.Logging;
using BGCS.CppAst.Parsing;
using Xunit;

namespace BGCS.Tests;

public class BindingCorrectnessMatrixTests
{
    public static IEnumerable<object[]> CAndCppCases()
    {
        yield return
        [
            "C primitives",
            """
            int Bgcs_Add(int a, int b);
            float Bgcs_Mix(float a, float b);
            void Bgcs_Reset(void);
            """,
            new[] { "EntryPoint = \"Bgcs_Add\"", "EntryPoint = \"Bgcs_Mix\"", "EntryPoint = \"Bgcs_Reset\"" },
            Array.Empty<string>()
        ];

        yield return
        [
            "C pointer params",
            """
            typedef struct BgcsPoint
            {
                int X;
                int Y;
            } BgcsPoint;

            void Bgcs_UpdatePoint(BgcsPoint* value);
            """,
            new[] { "EntryPoint = \"Bgcs_UpdatePoint\"", "BgcsUpdatePointNative" },
            Array.Empty<string>()
        ];

        yield return
        [
            "C string pointer mapping",
            """
            void Bgcs_Print(const char* text);
            void Bgcs_SetName(char* text);
            """,
            new[] { "EntryPoint = \"Bgcs_Print\"", "EntryPoint = \"Bgcs_SetName\"", "byte* text" },
            Array.Empty<string>()
        ];

        yield return
        [
            "C bool mapping",
            """
            void Bgcs_SetEnabled(bool value);
            bool Bgcs_IsEnabled(void);
            """,
            new[] { "EntryPoint = \"Bgcs_SetEnabled\"", "EntryPoint = \"Bgcs_IsEnabled\"" },
            Array.Empty<string>()
        ];

        yield return
        [
            "C enum + function",
            """
            typedef enum BgcsMode
            {
                BgcsMode_A = 0,
                BgcsMode_B = 1
            } BgcsMode;

            void Bgcs_SetMode(BgcsMode mode);
            """,
            new[] { "EntryPoint = \"Bgcs_SetMode\"", "enum BgcsMode" },
            Array.Empty<string>()
        ];

        yield return
        [
            "C callback typedef",
            """
            typedef void (*BgcsCallback)(int value);
            void Bgcs_SetCallback(BgcsCallback callback);
            """,
            new[] { "EntryPoint = \"Bgcs_SetCallback\"", "BgcsCallback" },
            Array.Empty<string>()
        ];

        yield return
        [
            "C macros as constants",
            """
            #define BGCS_CONST_A 42
            #define BGCS_CONST_B 0x10
            int Bgcs_GetConst(void);
            """,
            new[] { "EntryPoint = \"Bgcs_GetConst\"" },
            Array.Empty<string>()
        ];

        yield return
        [
            "C typedef alias",
            """
            typedef unsigned int BgcsUInt;
            BgcsUInt Bgcs_GetValue(void);
            void Bgcs_SetValue(BgcsUInt value);
            """,
            new[] { "EntryPoint = \"Bgcs_GetValue\"", "EntryPoint = \"Bgcs_SetValue\"" },
            Array.Empty<string>()
        ];

        yield return
        [
            "C++ extern C function in namespace unit",
            """
            namespace Demo
            {
                class IgnoredClass
                {
                public:
                    int Value;
                };
            }

            extern "C" int Bgcs_FromCpp(int a, int b);
            """,
            new[] { "EntryPoint = \"Bgcs_FromCpp\"" },
            new[] { "struct IgnoredClass" }
        ];

        yield return
        [
            "C++ extern C block",
            """
            extern "C"
            {
                int Bgcs_FromExternBlock(int a);
            }
            """,
            new[] { "EntryPoint = \"Bgcs_FromExternBlock\"", "BgcsFromExternBlock" },
            Array.Empty<string>()
        ];

        yield return
        [
            "C++ static function should not export",
            """
            static int Bgcs_StaticOnly(int a);
            int Bgcs_Normal(int a);
            """,
            new[] { "EntryPoint = \"Bgcs_Normal\"" },
            new[] { "EntryPoint = \"Bgcs_StaticOnly\"" }
        ];
    }

    public static IEnumerable<object[]> ComCases()
    {
        yield return
        [
            "COM vtbl-like structs",
            """
            typedef struct IMyInterfaceVtbl
            {
                void* QueryInterface;
                void* AddRef;
                void* Release;
            } IMyInterfaceVtbl;

            typedef struct IMyInterface
            {
                IMyInterfaceVtbl* lpVtbl;
            } IMyInterface;

            void Bgcs_UseCom(IMyInterface* value);
            """,
            new[] { "EntryPoint = \"Bgcs_UseCom\"", "IMyInterface" },
            Array.Empty<string>(),
            null
        ];

        yield return
        [
            "COM GUID extraction",
            """
            #define DEFINE_GUID(name, l, w1, w2, b1,b2,b3,b4,b5,b6,b7,b8)
            DEFINE_GUID(IID_IMyInterface, 0x12345678, 0x1234, 0x5678, 0x90, 0xab, 0xcd, 0xef, 0x12, 0x34, 0x56, 0x78);
            typedef struct IMyInterface IMyInterface;
            void Bgcs_UseComGuid(IMyInterface* value);
            """,
            new[] { "EntryPoint = \"Bgcs_UseComGuid\"" },
            Array.Empty<string>(),
            "IMyInterface"
        ];
    }

    [Theory]
    [MemberData(nameof(CAndCppCases))]
    public void Generate_CAndCpp_ShouldMatchExpectedFragments(string caseName, string header, string[] mustContain, string[] mustNotContain)
    {
        var run = Run(header, useComGenerator: false);
        try
        {
            AssertGeneratorSucceeded(run.Ok, run.Messages, caseName);

            foreach (string fragment in mustContain)
            {
                Assert.True(
                    run.GeneratedCode.Contains(fragment, StringComparison.Ordinal),
                    $"Case '{caseName}' expected fragment not found: {fragment}\nGenerated:\n{run.GeneratedCode}");
            }

            if (caseName == "C callback typedef")
            {
                bool hasTypedFunctionPointer = run.GeneratedCode.Contains("delegate*<int, void>", StringComparison.Ordinal)
                    || run.GeneratedCode.Contains("delegate*<void>", StringComparison.Ordinal);
                Assert.True(
                    hasTypedFunctionPointer,
                    $"Case '{caseName}' expected function pointer signature not found.\nGenerated:\n{run.GeneratedCode}");
            }

            foreach (string fragment in mustNotContain)
            {
                Assert.True(
                    !run.GeneratedCode.Contains(fragment, StringComparison.Ordinal),
                    $"Case '{caseName}' unexpected fragment found: {fragment}\nGenerated:\n{run.GeneratedCode}");
            }
        }
        finally
        {
            Cleanup(run.TempDir);
        }
    }

    [Theory]
    [MemberData(nameof(ComCases))]
    public void Generate_Com_ShouldMatchExpectedFragmentsAndGuids(string caseName, string header, string[] mustContain, string[] mustNotContain, string? expectedGuidName)
    {
        var run = Run(header, useComGenerator: true);
        try
        {
            AssertGeneratorSucceeded(run.Ok, run.Messages, caseName);

            foreach (string fragment in mustContain)
            {
                Assert.True(
                    run.GeneratedCode.Contains(fragment, StringComparison.Ordinal),
                    $"Case '{caseName}' expected fragment not found: {fragment}\nGenerated:\n{run.GeneratedCode}");
            }

            foreach (string fragment in mustNotContain)
            {
                Assert.True(
                    !run.GeneratedCode.Contains(fragment, StringComparison.Ordinal),
                    $"Case '{caseName}' unexpected fragment found: {fragment}\nGenerated:\n{run.GeneratedCode}");
            }

            if (expectedGuidName != null)
            {
                Assert.NotNull(run.ComGenerator);
                Assert.True(run.ComGenerator!.HasGUID(expectedGuidName), $"Case '{caseName}' expected GUID was not captured: {expectedGuidName}");
            }
        }
        finally
        {
            Cleanup(run.TempDir);
        }
    }

    private static void AssertGeneratorSucceeded(bool ok, IReadOnlyList<LogMessage> messages, string caseName)
    {
        Assert.True(ok);

        var failures = messages.Where(x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical).Select(x => x.ToString()).ToArray();
        if (failures.Length > 0)
        {
            Assert.True(false, $"Case '{caseName}' had generator errors:\n{string.Join("\n", failures)}");
        }
    }

    private static (bool Ok, string TempDir, string GeneratedCode, IReadOnlyList<LogMessage> Messages, CsComCodeGenerator? ComGenerator) Run(string headerText, bool useComGenerator)
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-matrix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string headerPath = Path.Combine(temp, "input.h");
        string outputPath = Path.Combine(temp, "out");
        File.WriteAllText(headerPath, headerText);

        CsCodeGeneratorConfig config = new()
        {
            ApiName = "Bgcs",
            Namespace = "BGCS.Tests.Generated",
            LibName = "bgcs_test",
            ImportType = ImportType.DllImport,
            GenerateExtensions = false,
            DelegatesAsVoidPointer = false
        };

        if (useComGenerator)
        {
            CsComCodeGenerator com = new(config);
            bool ok = com.Generate(CreateParserOptions(), headerPath, outputPath);
            return (ok, temp, ReadGeneratedCode(outputPath), com.Messages, com);
        }
        else
        {
            CsCodeGenerator gen = new(config);
            bool ok = gen.Generate(CreateParserOptions(), headerPath, outputPath);
            return (ok, temp, ReadGeneratedCode(outputPath), gen.Messages, null);
        }
    }

    private static CppParserOptions CreateParserOptions()
    {
        CppParserOptions options = new()
        {
            ParseMacros = true,
            ParseComments = true,
            ParseSystemIncludes = false,
            ParseCommentAttribute = true,
            ParserKind = CppParserKind.Cpp,
            AutoSquashTypedef = false
        };
        options.AdditionalArguments.Add("-undef");
        return options;
    }

    private static string ReadGeneratedCode(string outputPath)
    {
        if (!Directory.Exists(outputPath))
        {
            return string.Empty;
        }

        string[] files = Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories);
        return string.Join("\n\n", files.Select(File.ReadAllText));
    }

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
