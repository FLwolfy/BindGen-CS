using System;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;
using Xunit;

namespace BGCS.Tests;

public class GeneratorBuilderTests
{
    [Fact]
    public void WithMacros_ShouldAppendPrefixedDefines()
    {
        CsCodeGeneratorConfig config = new();

        GeneratorBuilder.Create<ProbeGenerator>(config)
            .WithMacros("BGCS_", m => m.AddMacro("A").AddMacro("B"))
            .GetConfig(out CsCodeGeneratorConfig result);

        Assert.Contains("BGCS_A", result.Defines);
        Assert.Contains("BGCS_B", result.Defines);
    }

    [Fact]
    public void OnPostConfigure_ShouldRunRegisteredCallback()
    {
        CsCodeGeneratorConfig config = new();
        bool called = false;
        string? apiName = null;

        GeneratorBuilder.Create<ProbeGenerator>(config)
            .OnPostConfigure((_, cfg) =>
            {
                called = true;
                apiName = cfg.ApiName;
            })
            .AlterConfig(cfg => cfg.ApiName = "ProbeApi")
            .AlterGenerator(g => ((ProbeGenerator)g).InvokeConfigureCore());

        Assert.True(called);
        Assert.Equal("ProbeApi", apiName);
    }

    private sealed class ProbeGenerator : CsCodeGenerator
    {
        public ProbeGenerator(CsCodeGeneratorConfig config) : base(config)
        {
        }

        public void InvokeConfigureCore()
        {
            ConfigureCore();
        }

        protected override CppCompilation ParseFiles(CppParserOptions parserOptions, System.Collections.Generic.List<string> headerFiles)
        {
            throw new NotSupportedException("Not used in this test.");
        }
    }
}
