using System;
using System.IO;
using System.Linq;
using BGCS.Configuration;
using BGCS.CppAst.Parsing;
using BGCS.Facade;
using Xunit;

namespace BGCS.Tests;

public sealed class ImportCallingConventionTests
{
    [Theory]
    [InlineData("windows-x86-msvc", true)]
    [InlineData("windows-x64-msvc", false)]
    [InlineData("linux-arm64-gnu", false)]
    [InlineData("emscripten-wasm32-emscripten", false)]
    public void LibraryImportPreservesAbiAndOmitsOnlyRedundantConvention(
        string target,
        bool requiresCdecl
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-import-abi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "api.h");
            File.WriteAllText(header, "int add(int left, int right);\n");
            var config = new CsCodeGeneratorConfig
            {
                targetId = target,
                parserKind = CppParserKind.C,
                apiName = "Api",
                @namespace = "Fixture",
                libName = "api",
                importType = ImportType.LibraryImport,
                singleFileOutputName = "Bindings.cs"
            };
            var generator = new CsCodeGenerator(config);
            Assert.True(generator.Generate(header, Path.Combine(root, "Generated")),
                string.Join(Environment.NewLine, generator.messages.Select(static value => value.message)));
            string source = string.Join(Environment.NewLine, generator.lastResult!.outputFiles.Select(File.ReadAllText));
            Assert.Equal("C", Assert.Single(generator.lastResult.module!.functions).callingConvention);
            Assert.Equal(requiresCdecl, source.Contains("[UnmanagedCallConv(", StringComparison.Ordinal));
            if (requiresCdecl)
                Assert.Contains("typeof(CallConvCdecl)", source, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
