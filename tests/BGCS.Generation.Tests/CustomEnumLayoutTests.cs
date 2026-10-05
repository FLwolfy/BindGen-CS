using System;
using System.IO;
using System.Linq;
using BGCS.Configuration;
using BGCS.CppAst.Parsing;
using BGCS.Facade;
using BGCS.Metadata;
using Xunit;

namespace BGCS.Tests;

public sealed class CustomEnumLayoutTests
{
    [Theory]
    [InlineData("byte", 1)]
    [InlineData("sbyte", 1)]
    [InlineData("short", 2)]
    [InlineData("ushort", 2)]
    [InlineData("int", 4)]
    [InlineData("uint", 4)]
    [InlineData("long", 8)]
    [InlineData("ulong", 8)]
    public void CustomEnumFactsMatchTheDeclaredIntegralCarrier(
        string carrier,
        int size
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-custom-enum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "api.h");
            File.WriteAllText(header, "int sample(int value);\n");
            CsCodeGeneratorConfig config = new()
            {
                parserKind = CppParserKind.C,
                apiName = "Api",
                @namespace = "Fixture",
                libName = "api",
                customEnums = [new CsEnumMetadata("NativeFlags", "Flags", [], null, carrier, [])]
            };
            CsCodeGenerator generator = new(config);
            Assert.True(generator.Generate(header, Path.Combine(root, "Generated")),
                string.Join(Environment.NewLine, generator.messages.Select(message => message.message)));
            var enumeration = Assert.Single(generator.lastResult!.module!.types);
            Assert.Equal(size, enumeration.size);
            Assert.Equal(size, enumeration.underlyingType!.size);
            Assert.Equal(carrier, enumeration.underlyingType.managedName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
