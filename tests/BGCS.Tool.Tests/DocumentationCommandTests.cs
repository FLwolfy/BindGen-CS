using System;
using System.IO;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class DocumentationCommandTests
{
    [Theory]
    [InlineData("/// <summary>Executes public operation Find.</summary>", "summary")]
    [InlineData("/// <summary>Finds the requested value.</summary>", "value")]
    [InlineData("/// <inheritdoc />", "Inheritdoc")]
    [InlineData("/// <summary>Performs the operation implemented by Find.</summary>\n/// <param name=\"value\">The lookup value.</param>\n/// <returns>The matching value.</returns>", "summary")]
    [InlineData("/// <summary>Returns the supplied value.</summary>\n/// <param name=\"wrong\">The lookup value.</param>\n/// <returns>The matching value.</returns>", "declared parameter")]
    [InlineData("/// <summary>Returns the supplied value.</summary>\n/// <param name=\"value\">The lookup value.</param>\n/// <returns>The result produced by Find.</returns>", "returns contract")]
    [InlineData("/// <summary>Returns the supplied value.</summary>\n/// <param name=\"value\">The lookup value.</param>\n/// <returns>Result produced by Find.</returns>", "returns contract")]
    public void IncompletePublicContractFailsWithAnActionableLocation(
        string documentation,
        string expectedMessage
    ) {
        WithSource("/// <summary>Provides value lookup for a consumer.</summary>\npublic sealed class Lookup {\n"
            + documentation + "\npublic int Find(int value) => value;\n}", (
                sourceRoot,
                output,
                error
            ) => {
                Assert.Equal(1, CliInvocation.Run(["validate", "documentation", sourceRoot], sourceRoot, output, error));
                Assert.Contains("Api.cs:", error.ToString());
                Assert.Contains(expectedMessage, error.ToString());
            });
    }

    [Fact]
    public void PrimaryConstructorRequiresParameterContracts()
    {
        WithSource("/// <summary>Captures an immutable lookup key.</summary>\npublic sealed record Key(string value);", (
            sourceRoot,
            output,
            error
        ) => {
            Assert.Equal(1, CliInvocation.Run(["validate", "documentation", sourceRoot], sourceRoot, output, error));
            Assert.Contains("'value' param", error.ToString());
        });
    }

    [Fact]
    public void CompletePublicContractIgnoresInternalImplementationAndGeneratedSources()
    {
        WithSource("""
            namespace DocumentationFixture;
            /// <summary>Returns values without changing the source collection.</summary>
            /// <typeparam name="T">The value type returned by this provider.</typeparam>
            public sealed class Lookup<T> {
                /// <summary>Returns the supplied value without retaining it.</summary>
                /// <param name="value">The value to return.</param>
                /// <returns>The same value, including null when T allows it.</returns>
                public T Find(T value) => value;
            }
            internal sealed class Implementation { public void Run(int ignored) { } }
            """, (
                sourceRoot,
                output,
                error
            ) => {
                string generated = Path.Combine(sourceRoot, "Generated");
                Directory.CreateDirectory(generated);
                File.WriteAllText(Path.Combine(generated, "Bindings.cs"), "public sealed class GeneratedApi { }");
                Assert.Equal(0, CliInvocation.Run(["validate", "documentation", sourceRoot], sourceRoot, output, error));
                Assert.Equal(string.Empty, error.ToString());
                Assert.Contains("0 violations", output.ToString());
            });
    }

    private static void WithSource(
        string source,
        Action<string, TextWriter, TextWriter> verify
    ) {
        string root = Path.Combine(Path.GetTempPath(), "BgcsDocumentation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Api.cs"), source);
            using var output = new StringWriter();
            using var error = new StringWriter();
            verify(root, output, error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
