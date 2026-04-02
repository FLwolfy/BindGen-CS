using System.Linq;
using System.Collections.Generic;
using System;
using System.IO;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;

namespace BGCS.CppAst.Tests;

public class InlineTestBase
{
    protected void ParseAssert(string text, Action<CppCompilation> assertCompilation, CppParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(assertCompilation);

        options ??= new CppParserOptions();
        var headerFilename = $"bgcs-cppast-{Guid.NewGuid():N}.h";
        var headerFile = Path.Combine(Environment.CurrentDirectory, headerFilename);

        var compilation = CppParser.Parse(text, options, headerFilename);
        foreach (var diagnosticsMessage in compilation.Diagnostics.Messages)
        {
            Console.WriteLine(diagnosticsMessage);
        }

        assertCompilation(compilation);

        File.WriteAllText(headerFile, text);
        compilation = CppParser.ParseFile(headerFile, options);
        assertCompilation(compilation);
    }
}
