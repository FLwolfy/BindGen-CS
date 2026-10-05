using System;
using System.IO;
using System.Linq;
using BGCS.Configuration;
using BGCS.CppAst.Parsing;
using BGCS.Facade;
using Xunit;

namespace BGCS.Tests;

public sealed class TargetVariadicTests
{
    [Theory]
    [InlineData("windows-x86-msvc", 4)]
    [InlineData("linux-arm64-gnu", 8)]
    public void ConfiguredVariadicCarriersUseTheNativeTargetPointerSize(
        string target,
        int pointerSize
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-variadic-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "api.h");
            File.WriteAllText(header, "void log_value(int category, ...);");
            var config = new CsCodeGeneratorConfig
            {
                targetId = target,
                parserKind = CppParserKind.C,
                apiName = "Api",
                @namespace = "Fixture",
                libName = "fixture"
            };
            config.variadicFunctionVariants["log_value"] = [new VariadicFunctionVariant
            {
                suffix = "Pointers",
                parameterTypes = ["nint", "nuint", "void*"]
            }];
            var generator = new CsCodeGenerator(config);
            Assert.True(generator.Generate(header, Path.Combine(root, "Generated")),
                string.Join(Environment.NewLine, generator.messages.Select(value => value.message)));
            Assert.All(Assert.Single(generator.lastResult!.module!.functions).parameters.Skip(1),
                parameter => Assert.Equal(pointerSize, parameter.type.size));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
