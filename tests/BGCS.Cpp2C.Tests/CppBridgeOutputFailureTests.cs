using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Cpp2C.Configuration;
using BGCS.Cpp2C.Facade;
using BGCS.Intermediate;
using BGCS.Intermediate.Bridges;
using BGCS.Intermediate.Emission;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class CppBridgeOutputFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutputFailuresRemainDistinctFromUnsupportedDeclarationsAndPreservePreviousOutput(bool denied)
    {
        string root = Path.Combine(Path.GetTempPath(), "BgcsOutputFailure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "api.hpp");
            string output = Path.Combine(root, "out");
            Directory.CreateDirectory(output);
            File.WriteAllText(header, "int bgcs_add(int left, int right);");
            File.WriteAllText(Path.Combine(output, "previous.txt"), "complete");
            var config = new Cpp2CGeneratorConfig { enableIncrementalCache = false };
            config.plugins.Register<ICppBridgeEmitter>("fixture.output-failure", new FailingEmitter(denied));
            var generator = new Cpp2CCodeGenerator(config);

            generator.Generate(header, output);

            Assert.False(generator.lastResult!.success);
            BindingDiagnostic diagnostic = Assert.Single(generator.lastResult.diagnostics,
                static value => value.code == BindingDiagnosticCodes.C_OUTPUTFAILURE);
            Assert.Contains("fixture write failure", diagnostic.message);
            Assert.DoesNotContain(generator.lastResult.diagnostics, static value => value.code == BindingDiagnosticCodes.C_CPPUNSUPPORTED);
            Assert.Equal("complete", File.ReadAllText(Path.Combine(output, "previous.txt")));
            Assert.Empty(Directory.GetDirectories(root, ".bgcs-staging-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SourceAndFrozenMetadataApplyTheSameExplicitHeaderBoundary()
    {
        string root = Path.Combine(Path.GetTempPath(), "BgcsHeaderBoundary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "api.hpp");
            string output = Path.Combine(root, "out");
            File.WriteAllText(Path.Combine(root, "dependency.hpp"), """
                template<typename T> class HiddenTemplate { public: T value; };
                class HiddenClass { public: int value; };
                enum HiddenEnum { HiddenValue };
                int hidden_function();
                """);
            File.WriteAllText(header, "#include \"dependency.hpp\"\nint bgcs_add(int left, int right);");
            var generator = new Cpp2CCodeGenerator(new Cpp2CGeneratorConfig { enableIncrementalCache = false });
            generator.Generate(header, output);
            Assert.True(generator.lastResult!.success);
            Assert.Equal("bgcs_add", Assert.Single(generator.lastResult.module!.functions).nativeName);
            Assert.Empty(generator.lastResult.module.types);
            Assert.DoesNotContain(generator.lastResult.diagnostics, static value => value.code == BindingDiagnosticCodes.C_CPPINSTANTIATION);
            Assert.DoesNotContain("HiddenEnum", File.ReadAllText(Path.Combine(output, "include", "enums.h")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FailingEmitter(bool denied) : ICppBridgeEmitter
    {
        public IReadOnlyList<string> Emit(
            CppBridgeModule module,
            EmissionContext context
        ) => throw (denied ? new UnauthorizedAccessException("fixture write failure") : new IOException("fixture write failure"));
    }
}
