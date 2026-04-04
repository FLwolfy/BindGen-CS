using System;
using System.Collections.Generic;
using System.IO;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;
using Xunit;

namespace BGCS.Tests;

public class BatchGeneratorTests
{
    [Fact]
    public void Generate_WithParserOptions_ShouldForwardOptionsToGenerator()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string configPath = Path.Combine(temp, "config.json");

        try
        {
            new CsCodeGeneratorConfig().Save(configPath);
            CppParserOptions options = new()
            {
                ParserKind = CppParserKind.C,
                ParseMacros = false,
                ParseComments = false
            };

            ProbeParseException ex = Assert.Throws<ProbeParseException>(() =>
                BatchGenerator.Create()
                    .Setup<ProbeGenerator>(configPath)
                    .Generate(options, "dummy.h", "out"));

            Assert.Same(options, ex.CapturedOptions);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    private sealed class ProbeParseException : Exception
    {
        public ProbeParseException(CppParserOptions capturedOptions)
        {
            CapturedOptions = capturedOptions;
        }

        public CppParserOptions CapturedOptions { get; }
    }

    private sealed class ProbeGenerator : CsCodeGenerator
    {
        public ProbeGenerator(CsCodeGeneratorConfig config) : base(config)
        {
        }

        protected override CppCompilation ParseFiles(CppParserOptions parserOptions, List<string> headerFiles)
        {
            throw new ProbeParseException(parserOptions);
        }
    }
}
