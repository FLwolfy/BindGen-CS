using System;
using System.IO;
using BGCS.Cpp2C.Emission;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class CppSourceWriterTests : IDisposable
{
    private readonly string m_directory = Path.Combine(Path.GetTempPath(), "bgcs-source-writer", Guid.NewGuid().ToString("N"));
    private readonly string m_path;

    public CppSourceWriterTests()
    {
        Directory.CreateDirectory(m_directory);
        m_path = Path.Combine(m_directory, "source.cpp");
    }

    [Fact]
    public void Fragments_PreserveMixedLineEndingsAndIndentEveryNewLine()
    {
        using (var writer = new CppSourceWriter(new StreamWriter(m_path), leaveOpen: false))
        {
            writer.Indent();
            writer.Write("alpha\r\nbeta\ngamma\rdelta");
            writer.Write('!');
            writer.Write('\n');
            writer.Write('z');
        }
        Assert.Equal("\talpha\r\n\tbeta\n\tgamma\r\tdelta!\n\tz", File.ReadAllText(m_path));
        Assert.NotEqual(0xef, File.ReadAllBytes(m_path)[0]);
    }

    [Fact]
    public void WriteLines_EmitsSeparateLinesWithoutMergingFragments()
    {
        using (var writer = new CppSourceWriter(new StreamWriter(m_path), leaveOpen: false))
        {
            writer.Indent();
            writer.WriteLines("a\r\nb\nc\rd");
            writer.WriteLines((string?)null);
            writer.WriteLines(["e", "f"]);
        }
        Assert.Equal(string.Join(Environment.NewLine, "\ta", "\tb", "\tc", "\td", "\te", "\tf", ""),
            File.ReadAllText(m_path));
    }

    [Fact]
    public void ScopedBlocks_CloseExactlyOnceAndReleaseTheFile()
    {
        var writer = new CppSourceWriter(new StreamWriter(m_path), leaveOpen: false);
        IDisposable scope = writer.PushBlock("void run()");
        writer.WriteLine("return;");
        scope.Dispose();
        scope.Dispose();
        writer.Dispose();
        writer.Dispose();
        scope.Dispose();
        Assert.Equal(string.Join(Environment.NewLine, "void run()", "{", "\treturn;", "}", ""),
            File.ReadAllText(m_path));
        using FileStream exclusive = File.Open(m_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<ObjectDisposedException>(() => writer.Write("late"));
    }

    [Fact]
    public void DisposalFailure_StillReleasesTheStream()
    {
        var writer = new CppSourceWriter(new StreamWriter(m_path), leaveOpen: false);
        writer.BeginBlock("void run()");
        writer.Unindent();
        Assert.Throws<ArgumentOutOfRangeException>(writer.Dispose);
        writer.Dispose();
        using FileStream exclusive = File.Open(m_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void UnbalancedBlocksAndIndentation_FailBeforeChangingOutputState()
    {
        using var writer = new CppSourceWriter(new StreamWriter(m_path), leaveOpen: false);
        Assert.Throws<InvalidOperationException>(() => writer.EndBlock());
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Indent(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Unindent());
        Assert.Equal(0, writer.indentLevel);
    }

    public void Dispose() => Directory.Delete(m_directory, recursive: true);
}
