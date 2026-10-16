using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Contracts.Artifacts;

namespace BGCS.Cpp2C.Emission;

/// <summary>
/// Formats source text with indentation into a caller-supplied text writer.
/// </summary>
/// <remarks>
/// The caller owns publication of the containing output directory. This writer is used only
/// on its owner thread and never publishes partially written files on its own.
/// </remarks>
public sealed class CppSourceWriter : ICodeWriter
{
    private readonly TextWriter m_writer;
    private readonly bool m_leaveOpen;
    private int m_blocks;
    private bool m_lineStart = true;
    private bool m_disposed;

    /// <summary>Formats source into a caller-owned text store without performing filesystem operations.</summary>
    /// <param name="writer">The candidate text writer.</param>
    /// <param name="leaveOpen">Whether the caller retains the writer's lifetime.</param>
    public CppSourceWriter(
        TextWriter writer,
        bool leaveOpen = true
    ) {
        ArgumentNullException.ThrowIfNull(writer);
        m_writer = writer;
        m_leaveOpen = leaveOpen;
    }

    /// <inheritdoc />
    public int indentLevel { get; private set; }

    /// <inheritdoc />
    public void BeginBlock(string content)
    {
        WriteLine(content);
        WriteLine("{");
        Indent();
        m_blocks++;
    }

    /// <inheritdoc />
    public void EndBlock(string marker = "}")
    {
        EnsureActive();
        if (m_blocks == 0)
            throw new InvalidOperationException("A source block cannot close without a matching opening block.");
        Unindent();
        m_blocks--;
        WriteLine(marker);
    }

    /// <inheritdoc />
    public void Indent(int count = 1)
    {
        EnsureActive();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        indentLevel = checked(indentLevel + count);
    }

    /// <inheritdoc />
    public void Unindent(int count = 1)
    {
        EnsureActive();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, indentLevel);
        indentLevel -= count;
    }

    /// <inheritdoc />
    public IDisposable PushBlock(string declaration)
    {
        BeginBlock(declaration);
        return new BlockScope(this);
    }

    /// <inheritdoc />
    public void Write(char chr)
    {
        EnsureActive();
        if (chr is not ('\r' or '\n'))
            WriteIndentation();
        m_writer.Write(chr);
        m_lineStart = chr is '\r' or '\n';
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(text);
        ReadOnlySpan<char> remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            int newline = remaining.IndexOfAny('\r', '\n');
            int length = newline < 0 ? remaining.Length : newline;
            if (length > 0)
            {
                WriteIndentation();
                m_writer.Write(remaining[..length]);
            }
            if (newline < 0)
                break;
            int separatorLength = remaining[newline] == '\r' && newline + 1 < remaining.Length
                && remaining[newline + 1] == '\n' ? 2 : 1;
            m_writer.Write(remaining.Slice(newline, separatorLength));
            m_lineStart = true;
            remaining = remaining[(newline + separatorLength)..];
        }
    }

    /// <inheritdoc />
    public void WriteLine()
    {
        EnsureActive();
        m_writer.WriteLine();
        m_lineStart = true;
    }

    /// <inheritdoc />
    public void WriteLine(string text)
    {
        Write(text);
        WriteLine();
    }

    /// <inheritdoc />
    public void WriteLines(string? text)
    {
        EnsureActive();
        if (text is null)
            return;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            WriteLine(line);
    }

    /// <inheritdoc />
    public void WriteLines(IEnumerable<string> lines)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(lines);
        foreach (string line in lines)
            WriteLine(line);
    }

    /// <summary>
    /// Closes outstanding blocks and releases the stream even if final output writing fails.
    /// </summary>
    /// <exception cref="IOException">
    /// Pending output cannot be written or flushed; the stream is still released.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Manual indentation changes prevent an outstanding block from closing.
    /// </exception>
    public void Dispose()
    {
        if (m_disposed)
            return;
        try
        {
            while (m_blocks > 0)
                EndBlock();
        }
        finally
        {
            m_disposed = true;
            if (!m_leaveOpen)
                m_writer.Dispose();
        }
    }

    private void EnsureActive() => ObjectDisposedException.ThrowIf(m_disposed, this);

    private void WriteIndentation()
    {
        if (!m_lineStart)
            return;
        for (int index = 0; index < indentLevel; index++)
            m_writer.Write('\t');
        m_lineStart = false;
    }

    private sealed class BlockScope(CppSourceWriter owner) : IDisposable
    {
        private CppSourceWriter? m_owner = owner;

        public void Dispose()
        {
            CppSourceWriter? owner = m_owner;
            m_owner = null;
            if (owner is { m_disposed: false })
                owner.EndBlock();
        }
    }
}
