using System;
using System.IO;
using BGCS.Configuration;
using BGCS.CppAst.Parsing;
using Xunit;

namespace BGCS.Tests;

public class DocumentationGenerationTests
{
    [Fact]
    public void WriteCsSummary_DoxygenCommands_ShouldEmitStructuredXmlDocumentation()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "docs.h");
        File.WriteAllText(header,
            """
            /** Computes a value.
             * @param value Input value less than 10.
             * @return The computed result.
             */
            int bgcs_compute(int value);
            """);

        try
        {
            CppParserOptions options = new()
            {
                parseComments = true,
                parseSystemIncludes = false,
                parseCommentAttribute = true,
                parserKind = CppParserKind.C
            };
            var compilation = CppParser.ParseFile(header, options);
            CsCodeGeneratorConfig config = new() { generatePlaceholderComments = false };

            config.WriteCsSummary(compilation.functions[0].comment, out string? documentation);

            Assert.Contains("<summary>", documentation);
            Assert.Contains("Computes a value.", documentation);
            Assert.Contains("<param name=\"value\">Input value less than 10.</param>", documentation);
            Assert.Contains("<returns>The computed result.</returns>", documentation);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
