using System;
using System.IO;
using BGCS.Frontend.Clang.Model.Metadata;
using BGCS.Frontend.Clang.Parsing;

namespace BGCS.Frontend.Clang.Tests;

public class InlineTestBase
{
    private protected void ParseAssert(
        string text,
        Action<CppCompilation> assertCompilation,
        CppParserOptions? options = null
    ) {
        ArgumentNullException.ThrowIfNull(assertCompilation);
        options ??= new CppParserOptions { targetTriple = "x86_64-pc-windows-msvc" };
        string headerFilename = $"bgcs-cppast-{Guid.NewGuid():N}.h";
        string headerFile = Path.Combine(Environment.CurrentDirectory, headerFilename);

        using (CppCompilation compilation = CppParser.Parse(text, options, headerFilename))
        {
            foreach (var diagnostic in compilation.diagnostics.messages)
                Console.WriteLine(diagnostic);
            assertCompilation(compilation);
        }

        try
        {
            File.WriteAllText(headerFile, text);
            using CppCompilation compilation = CppParser.ParseFile(headerFile, options);
            assertCompilation(compilation);
        }
        finally
        {
            File.Delete(headerFile);
        }
    }
}
