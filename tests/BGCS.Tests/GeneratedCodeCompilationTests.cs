using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using BGCS.Core.Logging;
using BGCS.CppAst.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public class GeneratedCodeCompilationTests
{
    [Fact]
    public void Generate_CBindings_OutputShouldCompileAndContainExpectedSymbols()
    {
        string header = """
            typedef struct MyStruct
            {
                int a;
            } MyStruct;

            int test_fn(int value);
            """;

        var run = RunGenerator(header, useComGenerator: false);
        try
        {
            AssertGeneratorSucceeded(run.Ok, run.Messages);

            Assembly asm = CompileGeneratedSources(run.OutputPath, "BGCS.Generated.C");
            Type? structType = asm.GetType("Compile.Generated.MyStruct");
            Assert.NotNull(structType);
            Assert.Contains(structType!.GetFields(BindingFlags.Public | BindingFlags.Instance), x => x.FieldType == typeof(int));

            MethodInfo? method = asm
                .GetTypes()
                .SelectMany(x => x.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                .FirstOrDefault(x => x.Name == "TestFnNative");

            Assert.NotNull(method);
            Assert.Equal(typeof(int), method!.ReturnType);
        }
        finally
        {
            Cleanup(run.TempDirectory);
        }
    }

    [Fact]
    public void Generate_ComBindings_OutputShouldCompileAndContainExpectedSymbols()
    {
        string header = """
            #define DEFINE_GUID(name, l, w1, w2, b1,b2,b3,b4,b5,b6,b7,b8)
            DEFINE_GUID(IID_IMyInterface, 0x12345678, 0x1234, 0x5678, 0x90, 0xab, 0xcd, 0xef, 0x12, 0x34, 0x56, 0x78);

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

            void test_use_com(IMyInterface* value);
            """;

        var run = RunGenerator(header, useComGenerator: true);
        try
        {
            AssertGeneratorSucceeded(run.Ok, run.Messages);

            Assembly asm = CompileGeneratedSources(run.OutputPath, "BGCS.Generated.COM");
            Type? comType = asm.GetType("Compile.Generated.IMyInterface");
            Assert.NotNull(comType);
            Assert.Contains(
                comType!.GetFields(BindingFlags.Public | BindingFlags.Instance),
                x => x.FieldType.IsPointer || x.FieldType == typeof(nint));

            MethodInfo? method = asm
                .GetTypes()
                .SelectMany(x => x.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                .FirstOrDefault(x => x.Name == "TestUseComNative");

            Assert.NotNull(method);
            Assert.Equal(typeof(void), method!.ReturnType);
        }
        finally
        {
            Cleanup(run.TempDirectory);
        }
    }

    private static Assembly CompileGeneratedSources(string outputPath, string assemblyName)
    {
        string[] sourceFiles = Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sourceFiles);

        SyntaxTree[] trees = sourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToArray();

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        using MemoryStream pe = new();
        var emit = compilation.Emit(pe);

        if (!emit.Success)
        {
            string diagnostics = string.Join(
                Environment.NewLine,
                emit.Diagnostics
                    .Where(x => x.Severity == DiagnosticSeverity.Error)
                    .Select(x => x.ToString()));
            Assert.Fail("Generated code compilation failed:\n" + diagnostics);
        }

        pe.Position = 0;
        AssemblyLoadContext alc = new("BGCS.Generated.Test", isCollectible: true);
        return alc.LoadFromStream(pe);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(tpa))
        {
            throw new InvalidOperationException("Missing TRUSTED_PLATFORM_ASSEMBLIES.");
        }

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in tpa.Split(Path.PathSeparator))
        {
            paths.Add(path);
        }

        paths.Add(typeof(CsCodeGenerator).Assembly.Location);
        paths.Add(typeof(CsCodeGeneratorConfig).Assembly.Location);

        return paths.Select(static path => MetadataReference.CreateFromFile(path));
    }

    private static void AssertGeneratorSucceeded(bool ok, IReadOnlyList<LogMessage> messages)
    {
        Assert.True(ok);
        Assert.DoesNotContain(messages, x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);
    }

    private static (bool Ok, string TempDirectory, string OutputPath, IReadOnlyList<LogMessage> Messages) RunGenerator(string headerText, bool useComGenerator)
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-compile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string headerPath = Path.Combine(temp, "input.h");
        string outputPath = Path.Combine(temp, "out");
        File.WriteAllText(headerPath, headerText);

        CsCodeGeneratorConfig cfg = new()
        {
            ApiName = "CompileApi",
            Namespace = "Compile.Generated",
            LibName = "compiletest",
            GenerateExtensions = false,
            ImportType = ImportType.DllImport,
            DelegatesAsVoidPointer = false
        };

        BaseGenerator generator = useComGenerator ? new CsComCodeGenerator(cfg) : new CsCodeGenerator(cfg);

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

        bool ok = useComGenerator
            ? ((CsComCodeGenerator)generator).Generate(parserOptions, headerPath, outputPath)
            : ((CsCodeGenerator)generator).Generate(parserOptions, headerPath, outputPath);

        return (ok, temp, outputPath, generator.Messages);
    }

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
