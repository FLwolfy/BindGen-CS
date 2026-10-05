using System;
using System.IO;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class CSharpStyleCommandTests
{
    [Fact]
    public void Fix_PreservesInactiveRawLiteralBytesAndDirectivePlacement()
    {
        using SourceDirectory directory = new();
        string inactive = "public class Custom { public string value = \"\"\"\r\n    first\r\n    second\r\n    \"\"\"; }\r\n";
        directory.Write("#if CUSTOM\r\n" + inactive + "#else\r\npublic class Standard { public void Execute(int first, int second) { } }\r\n#endif\r\n");

        Assert.Equal(0, directory.Run("--fix"));
        string formatted = directory.Read();
        Assert.Contains(inactive, formatted);
        Assert.True(formatted.IndexOf("#if CUSTOM", StringComparison.Ordinal)
            < formatted.IndexOf(inactive, StringComparison.Ordinal));
        Assert.True(formatted.IndexOf(inactive, StringComparison.Ordinal)
            < formatted.IndexOf("#else", StringComparison.Ordinal));
        Assert.Equal(0, directory.Run());
    }

    [Fact]
    public void Fix_ExpandsDeclarationsAndPreservesCommentsAndTokens()
    {
        using SourceDirectory directory = new();
        directory.Write("""
            namespace Fixture;
            public sealed class Example {
                public Example(int first, int second) { }
                public int Sum(int first, /* parameter */ int second) { return first + second; }
                public static Example operator +(Example left, Example right) { return left; }
                public void Execute() {
                    int Local(int first, int second) { return first + second; }
                    System.Func<int, int, int> lambda = (first, second) => { return Local(first, second); };
                }
            }
            """);

        Assert.Equal(1, directory.Run());
        Assert.Equal(0, directory.Run("--fix"));
        string formatted = directory.Read();
        Assert.Contains("public int Sum(\n        int first, /* parameter */\n        int second\n    ) {", formatted);
        Assert.Contains("public Example(\n        int first,\n        int second\n    ) {", formatted);
        Assert.Contains("int Local(\n            int first,\n            int second\n        ) {", formatted);
        Assert.Contains("(\n            first,\n            second\n        ) => {", formatted);
        Assert.Contains("operator +(\n        Example left,\n        Example right\n    ) {", formatted);
        Assert.Equal(0, directory.Run());
        Assert.Equal(0, directory.Run("--fix"));
        Assert.Equal(formatted, directory.Read());
    }

    [Fact]
    public void Fix_PreservesConditionalCompilationAndLineComments()
    {
        using SourceDirectory directory = new();
        directory.Write("""
            #if CUSTOM
            public class Custom { }
            #else
            public class Standard {
                public void Execute(int first, // first remains documented
                    int second) { }
            }
            #endif
            """);

        Assert.Equal(0, directory.Run("--fix"));
        string formatted = directory.Read();
        Assert.Contains("// first remains documented", formatted);
        Assert.Contains("public class Custom { }", formatted);
        Assert.Equal(0, directory.Run());
    }

    [Fact]
    public void Fix_InvalidSource_IsRejectedWithoutWriting()
    {
        using SourceDirectory directory = new();
        const string invalidSource = "public class Invalid { void Run(int first, int second) {";
        directory.Write(invalidSource);

        Assert.Equal(2, directory.Run("--fix"));
        Assert.Equal(invalidSource, directory.Read());
    }

    [Fact]
    public void Fix_PreservesRawStringLineEndingsAndRemainsIdempotent()
    {
        using SourceDirectory directory = new();
        string source = "public class Template { public string text = \"\"\"\r\n    first\r\n    second\r\n    \"\"\"; }";
        directory.Write(source);

        Assert.Equal(0, directory.Run("--fix"));
        string formatted = directory.Read();
        Assert.Contains("\"\"\"\r\n    first\r\n    second\r\n    \"\"\"", formatted);
        Assert.Equal(0, directory.Run());
        Assert.Equal(0, directory.Run("--fix"));
        Assert.Equal(formatted, directory.Read());
    }

    [Fact]
    public void Fix_PreservesAuthoredCallGroupingAndNullSuppression()
    {
        using SourceDirectory directory = new();
        directory.Write("""
            public class Example
            {
                /// <summary>Uses <see cref="string"/> without changing the documentation.</summary>
                public void Execute()
                {
                    Accept(
                        "first", "second",
                        null!);
                }
                private void Accept(string first, string second, object third) { }
            }
            """);

        Assert.Equal(0, directory.Run("--fix"));
        string formatted = directory.Read();
        Assert.Contains("\"first\", \"second\",\n", formatted.ReplaceLineEndings("\n"));
        Assert.Contains("null!", formatted);
        Assert.Contains("<see cref=\"string\"/>", formatted);
        Assert.Equal(0, directory.Run());
    }

    [Fact]
    public void Validate_ExcludesGeneratedSourceAndBuildOutputs()
    {
        using SourceDirectory directory = new();
        foreach (string excluded in new[] { "obj", "bin", "Generated", "extern" })
        {
            string path = Path.Combine(directory.path, excluded);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "Invalid.cs"), "intentionally invalid source");
        }
        directory.Write("namespace Fixture;\n\npublic class Empty\n{\n}\n");

        Assert.Equal(0, directory.Run());
    }

    private sealed class SourceDirectory : IDisposable
    {
        internal string path { get; } = Path.Combine(Path.GetTempPath(), "bgcs-style-" + Guid.NewGuid().ToString("N"));

        internal SourceDirectory() => Directory.CreateDirectory(path);

        internal void Write(string source) => File.WriteAllText(Path.Combine(path, "Example.cs"), source);

        internal string Read() => File.ReadAllText(Path.Combine(path, "Example.cs"));

        internal int Run(params string[] options)
        {
            using StringWriter output = new();
            using StringWriter error = new();
            return CliInvocation.Run(["validate", "style", path, .. options], path, output, error);
        }

        public void Dispose() => Directory.Delete(path, recursive: true);
    }
}
