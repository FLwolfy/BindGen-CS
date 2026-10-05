using System;
using System.Collections.Generic;
using BGCS.Core.Writing;
using BGCS.Intermediate.Bridges;

namespace BGCS.Cpp2C.Analysis;

/// <summary>
/// Collects lowered source operations while AST-dependent analysis is still active.
/// </summary>
internal sealed class CppBridgeOperationBuilder : ICodeWriter, IDisposable
{
    private readonly List<CppBridgeOperation> m_operations = [];
    private int m_blockCount;
    private bool m_completed;
    public int indentLevel { get; private set; }

    internal CppBridgeOperationBuilder(string preamble = "")
    {
        if (preamble.Length > 0)
            Write(preamble);
    }

    public void BeginBlock(string content)
    {
        EnsureActive();
        m_operations.Add(new(CppBridgeOperationKind.BeginBlock, content));
        this.indentLevel++;
        m_blockCount++;
    }

    public void EndBlock(string marker = "}")
    {
        EnsureActive();
        if (m_blockCount == 0)
            throw new InvalidOperationException("Bridge lowering attempted to close a block that was not opened.");
        m_operations.Add(new(CppBridgeOperationKind.EndBlock, marker));
        this.indentLevel--;
        m_blockCount--;
    }

    public void Indent(int count = 1)
    {
        EnsureActive();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        m_operations.Add(new(CppBridgeOperationKind.Indent, count: count));
        this.indentLevel += count;
    }

    public void Unindent(int count = 1)
    {
        EnsureActive();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > this.indentLevel)
            throw new ArgumentOutOfRangeException(nameof(count), "Bridge indentation cannot become negative.");
        m_operations.Add(new(CppBridgeOperationKind.Unindent, count: count));
        this.indentLevel -= count;
    }

    public IDisposable PushBlock(string declaration)
    {
        BeginBlock(declaration);
        return new BlockScope(this);
    }

    public void Write(char chr) => Write(chr.ToString());
    public void Write(string @string)
    {
        EnsureActive();
        m_operations.Add(new(CppBridgeOperationKind.Fragment, @string));
    }

    public void WriteLine() => WriteLine(string.Empty);
    public void WriteLine(string @string)
    {
        EnsureActive();
        m_operations.Add(new(CppBridgeOperationKind.Line, @string));
    }

    public void WriteLines(string? @string)
    {
        if (@string is null)
            return;
        foreach (string line in @string.Split('\n'))
            WriteLine(line.TrimEnd('\r'));
    }

    public void WriteLines(IEnumerable<string> lines)
    {
        foreach (string line in lines)
            WriteLine(line);
    }

    public void Dispose()
    {
        if (m_blockCount != 0 || this.indentLevel != 0)
            throw new InvalidOperationException("Bridge lowering left an unbalanced block or indentation scope.");
        m_completed = true;
    }

    internal CppBridgeArtifact Freeze(string relativePath)
    {
        Dispose();
        return new(relativePath, m_operations);
    }

    private void EnsureActive()
    {
        if (m_completed)
            throw new InvalidOperationException("A frozen bridge operation builder cannot accept further source operations.");
    }

    private sealed class BlockScope : IDisposable
    {
        private CppBridgeOperationBuilder? m_owner;
        internal BlockScope(CppBridgeOperationBuilder owner) => m_owner = owner;
        public void Dispose()
        {
            m_owner?.EndBlock();
            m_owner = null;
        }
    }
}
