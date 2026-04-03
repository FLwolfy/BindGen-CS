using System.Linq;
using System.IO;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Parsing;
using Xunit;

namespace BGCS.Tests;

public class ExternCDetectionTests
{
    [Fact]
    public void Parse_ExternCDeclaration_ShouldMarkFunctionAsExternC()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-externc-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string file = Path.Combine(temp, "extern_c.hpp");
        File.WriteAllText(file, "extern \"C\" int Bgcs_Extern(int a);");

        try
        {
            CppParserOptions options = new()
            {
                ParserKind = CppParserKind.Cpp,
                ParseMacros = true,
                ParseSystemIncludes = false
            };
            options.AdditionalArguments.Add("-undef");

            var compilation = CppParser.ParseFile(file, options);
            var function = compilation.Functions.FirstOrDefault(x => x.Name == "Bgcs_Extern");

            Assert.True(
                function != null,
                $"Function not found. Diagnostics:\n{compilation.Diagnostics}\nFunctions: {string.Join(", ", compilation.Functions.Select(x => x.Name))}");
            Assert.True(function!.IsExternC);
            Assert.True(function.IsPublicExport());
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    [Fact]
    public void Parse_NormalCppDeclaration_ShouldNotMarkExternC()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-normalcpp-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string file = Path.Combine(temp, "normal_cpp.hpp");
        File.WriteAllText(file, "int Bgcs_Normal(int a);");

        try
        {
            CppParserOptions options = new()
            {
                ParserKind = CppParserKind.Cpp,
                ParseMacros = true,
                ParseSystemIncludes = false
            };
            options.AdditionalArguments.Add("-undef");

            var compilation = CppParser.ParseFile(file, options);
            var function = compilation.Functions.FirstOrDefault(x => x.Name == "Bgcs_Normal");

            Assert.True(
                function != null,
                $"Function not found. Diagnostics:\n{compilation.Diagnostics}\nFunctions: {string.Join(", ", compilation.Functions.Select(x => x.Name))}");
            Assert.False(function!.IsExternC);
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
