using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Configuration;
using BGCS.Core.Logging;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;
using BGCS.Facade;
using Xunit;

namespace BGCS.Tests;

public sealed class GenerationLifetimeTests : IDisposable
{
    private readonly string m_directory = Path.Combine(Path.GetTempPath(), "bgcs-generation-lifetime", Guid.NewGuid().ToString("N"));
    private readonly string m_header;
    private readonly string m_output;

    public GenerationLifetimeTests()
    {
        Directory.CreateDirectory(m_directory);
        m_header = Path.Combine(m_directory, "api.h");
        m_output = Path.Combine(m_directory, "Generated");
    }

    [Fact]
    public void RepeatedAttempts_ReplaceDiagnosticsAndRetainOnlyTheCurrentResult()
    {
        CsCodeGeneratorConfig config = CreateConfig();
        config.cppLogLevel = LogSeverity.Critical;
        config.logLevel = LogSeverity.Critical;
        var generator = new CsCodeGenerator(config);
        File.WriteAllText(m_header, "int broken(;");
        Assert.False(generator.GenerateConfigured());
        Assert.False(generator.lastResult!.success);
        Assert.Contains(generator.lastResult.diagnostics, diagnostic => diagnostic.severity >= BGCS.Intermediate.BindingDiagnosticSeverity.Error);

        File.WriteAllText(m_header, "int bgcs_add(int value);");
        Assert.True(generator.GenerateConfigured());
        Assert.True(generator.lastResult!.success);
        Assert.DoesNotContain(generator.messages, diagnostic => diagnostic.severity >= LogSeverity.Error);
        Assert.DoesNotContain(generator.lastResult.diagnostics, diagnostic => diagnostic.severity >= BGCS.Intermediate.BindingDiagnosticSeverity.Error);

        config.entryFiles.Clear();
        Assert.Throws<InvalidOperationException>(() => generator.GenerateConfigured());
        Assert.Null(generator.lastResult);
        Assert.Empty(generator.messages);
    }

    [Fact]
    public void PublicParserEntry_PreservesTheProvidedOptionsAndClearsStaleResultsOnFailure()
    {
        File.WriteAllText(m_header, "int bgcs_add(int value);");
        var generator = new FailingParserGenerator(CreateConfig());
        var options = new CppParserOptions { parserKind = CppParserKind.C };
        bool configured = false;
        generator.PostConfigure += (
            sender,
            config
        ) =>
        {
            Assert.Same(generator, sender);
            Assert.Same(generator.settings, config);
            configured = true;
        };
        Assert.True(generator.GenerateConfigured());
        Assert.True(configured);

        generator.fail = true;
        Assert.Throws<IOException>(() => generator.Generate(options, m_header, m_output));
        Assert.Same(options, generator.observedOptions);
        Assert.Null(generator.lastResult);
    }

    private CsCodeGeneratorConfig CreateConfig() => new()
    {
        apiName = "LifetimeApi",
        @namespace = "BGCS.Tests.Generated",
        libName = "lifetime",
        entryFiles = [m_header],
        outputPath = m_output,
        parserKind = CppParserKind.C,
        generateExtensions = false,
        parseMacros = false,
        parseComments = false,
        parseSystemIncludes = false,
        generateRuntimeSource = false
    };

    public void Dispose() => Directory.Delete(m_directory, recursive: true);

    private sealed class FailingParserGenerator(CsCodeGeneratorConfig config) : CsCodeGenerator(config)
    {
        public bool fail { get; set; }
        public CppParserOptions? observedOptions { get; private set; }

        protected override CppCompilation ParseFiles(
            CppParserOptions parserOptions,
            List<string> headerFiles
        ) {
            observedOptions = parserOptions;
            if (fail)
                throw new IOException("injected parser failure");
            return base.ParseFiles(parserOptions, headerFiles);
        }
    }
}
