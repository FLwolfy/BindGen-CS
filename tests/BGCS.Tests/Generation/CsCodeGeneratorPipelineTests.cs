using System;
using System.Collections.Generic;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;
using Xunit;

namespace BGCS.Tests;

public class CsCodeGeneratorPipelineTests
{
    [Fact]
    public void ConfigureCore_RepeatedCalls_ShouldNotDuplicateSteps()
    {
        ProbeGenerator generator = new(new CsCodeGeneratorConfig());

        generator.InvokeConfigureCore();
        int firstPreCount = generator.PreProcessSteps.Count;
        int firstStepCount = generator.GenerationSteps.Count;

        generator.InvokeConfigureCore();

        Assert.Equal(firstPreCount, generator.PreProcessSteps.Count);
        Assert.Equal(firstStepCount, generator.GenerationSteps.Count);
    }

    [Fact]
    public void Generate_WithParserOptionsAndSingleHeader_ShouldForwardExactOptions()
    {
        ProbeGenerator generator = new(new CsCodeGeneratorConfig());
        CppParserOptions options = new()
        {
            ParseMacros = false,
            ParseComments = false,
            ParseSystemIncludes = false,
            ParserKind = CppParserKind.C
        };

        ProbeParseException ex = Assert.Throws<ProbeParseException>(
            () => generator.Generate(options, "dummy.h", "out"));

        Assert.Same(options, ex.CapturedOptions);
        Assert.Same(options, generator.CapturedOptions);
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

        public CppParserOptions? CapturedOptions { get; private set; }

        public void InvokeConfigureCore()
        {
            ConfigureCore();
        }

        protected override CppCompilation ParseFiles(CppParserOptions parserOptions, List<string> headerFiles)
        {
            CapturedOptions = parserOptions;
            throw new ProbeParseException(parserOptions);
        }
    }
}
