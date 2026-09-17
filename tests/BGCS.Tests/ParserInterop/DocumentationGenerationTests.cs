using System;
using System.IO;
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
                ParseComments = true,
                ParseSystemIncludes = false,
                ParseCommentAttribute = true,
                ParserKind = CppParserKind.C
            };
            var compilation = CppParser.ParseFile(header, options);
            CsCodeGeneratorConfig config = new() { GeneratePlaceholderComments = false };

            config.WriteCsSummary(compilation.Functions[0].Comment, out string? documentation);

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
