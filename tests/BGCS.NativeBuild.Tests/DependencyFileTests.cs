using System;
using System.Collections.Immutable;
using System.IO;
using BGCS.NativeBuild.Execution;
using BGCS.Contracts.Requests;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class DependencyFileTests
{
    [Fact]
    public void MakeDependencyEscapingPreservesFilesystemCharacters()
    {
        ImmutableArray<string> inputs = BuildDependencyCollector.ParseMake("output.o: /source/hello\\ world.h /source/a\\#b.h /source/dollar$$value.h \\\n /source/Unicode名字.h /source/50%.h\n");
        Assert.Equal(new[] { "/source/hello world.h", "/source/a#b.h", "/source/dollar$value.h", "/source/Unicode名字.h", "/source/50%.h" }, inputs);
    }
    [Fact]
    public void MakeWindowsDrivePrefixIsNotMistakenForRuleSeparator()
    {
        Assert.Equal(new[] { "C:\\input\\source.h", "D:/sdk/a b.h" }, BuildDependencyCollector.ParseMake("C:/output.o: C:\\input\\source.h D:/sdk/a\\ b.h\n"));
    }
    [Theory]
    [InlineData("this has no rule separator")]
    [InlineData("output.o:\n")]
    public void IncompleteDependencyRuleIsRejected(string text) => Assert.Throws<System.IO.InvalidDataException>(() => BuildDependencyCollector.ParseMake(text));

    [Theory]
    [InlineData("relative")]
    [InlineData("gnu-archive")]
    [InlineData("llvm-archive")]
    [InlineData("literal-parentheses")]
    public void LinkerTracePathsUseTheFrozenInvocationDirectory(string syntax)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs linker trace 目录 & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string relative = syntax == "literal-parentheses" ? "native object(copy)" : "native archive.a";
            string input = Path.Combine(root, relative);
            File.WriteAllText(input, "content identity belongs to this invocation");
            string trace = syntax switch
            {
                "gnu-archive" => "(" + relative + ")member.o",
                "llvm-archive" => relative + "(member.o)",
                _ => relative
            };
            Assert.NotEqual(Path.GetFullPath(root), Path.GetFullPath(Directory.GetCurrentDirectory()));
            Assert.Equal(new[] { input }, BuildDependencyCollector.ReadLinkTrace(trace, NativeLinkTraceFormat.GnuLike, root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LinkerTraceCannotSilentlyOmitAMissingAbsoluteResolvedInput()
    {
        string missing = Path.Combine(Path.GetTempPath(), "bgcs missing linker input " + Guid.NewGuid().ToString("N"), "native.a");
        Assert.Throws<FileNotFoundException>(() => BuildDependencyCollector.ReadLinkTrace(missing, NativeLinkTraceFormat.GnuLike, Path.GetTempPath()));
    }
}
