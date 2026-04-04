using System.Collections.Generic;
using System.Linq;
using System.IO;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
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

    [Fact]
    public void Parse_ExternCBlockInsideNamespace_ShouldMarkFunctionAsExternC()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-externc-ns-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string file = Path.Combine(temp, "extern_c_ns.hpp");
        File.WriteAllText(file,
            """
            namespace Demo
            {
                extern "C"
                {
                    int Bgcs_ExternNs(int a);
                }
            }
            """);

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
            var function = FindFunctionByName(compilation, "Bgcs_ExternNs");
            Assert.True(
                function != null,
                $"Function not found. Diagnostics:\n{compilation.Diagnostics}\nFunctions: {string.Join(", ", EnumerateFunctions(compilation).Select(x => x.Name))}");
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
    public void Parse_ClassMethodInsideExternCBlock_ShouldNotBeMarkedAsPublicExport()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-externc-class-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string file = Path.Combine(temp, "extern_c_class.hpp");
        File.WriteAllText(file,
            """
            extern "C"
            {
                class DemoClass
                {
                public:
                    int Bgcs_Method(int a);
                };
            }
            """);

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
            Assert.DoesNotContain(compilation.Functions, x => x.Name == "Bgcs_Method" && x.IsPublicExport());
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
    public void Parse_MixedExternCAndNormalFunctions_ShouldMarkOnlyExternAsExternC()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-externc-mixed-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string file = Path.Combine(temp, "extern_c_mixed.hpp");
        File.WriteAllText(file,
            """
            int Bgcs_Normal(int a);

            extern "C"
            {
                int Bgcs_ExternOnly(int a);
            }

            namespace Demo
            {
                int Bgcs_NamespaceOnly(int a);
            }
            """);

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
            var normal = FindFunctionByName(compilation, "Bgcs_Normal");
            var externOnly = FindFunctionByName(compilation, "Bgcs_ExternOnly");
            var nsOnly = FindFunctionByName(compilation, "Bgcs_NamespaceOnly");

            Assert.NotNull(normal);
            Assert.NotNull(externOnly);
            Assert.NotNull(nsOnly);

            Assert.False(normal!.IsExternC);

            Assert.True(externOnly!.IsExternC);
            Assert.True(externOnly.IsPublicExport());

            Assert.False(nsOnly!.IsExternC);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    private static BGCS.CppAst.Model.Declarations.CppFunction? FindFunctionByName(BGCS.CppAst.Model.Metadata.CppCompilation compilation, string unqualifiedName)
    {
        return EnumerateFunctions(compilation).FirstOrDefault(
            x => x.Name == unqualifiedName || x.Name.EndsWith("::" + unqualifiedName, System.StringComparison.Ordinal));
    }

    private static IEnumerable<CppFunction> EnumerateFunctions(ICppGlobalDeclarationContainer container)
    {
        foreach (var function in container.Functions)
        {
            yield return function;
        }

        foreach (var ns in container.Namespaces)
        {
            foreach (var function in EnumerateFunctions(ns))
            {
                yield return function;
            }
        }
    }
}
