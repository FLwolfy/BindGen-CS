using System;
using System.Collections.Generic;

namespace BGCS.Core.Writing;

/// <summary>
/// Writes source fragments and scoped indentation through a single caller-owned output lifetime.
/// </summary>
/// <remarks>
/// Implementations may record operations instead of writing files. Calls belong to the owner thread.
/// </remarks>
public interface ICodeWriter : IDisposable
{
    /// <summary>
    /// Gets the current nonnegative indentation depth.
    /// </summary>
    int indentLevel { get; }

    /// <summary>
    /// Writes a declaration followed by an opening brace and increases indentation.
    /// </summary>
    /// <param name="content">
    /// The declaration or source text preceding the opening brace.
    /// </param>
    void BeginBlock(string content);

    /// <summary>
    /// Closes the most recently opened block and decreases indentation.
    /// </summary>
    /// <param name="marker">
    /// Closing source text, including any required punctuation.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// No matching block is open.
    /// </exception>
    void EndBlock(string marker = "}");

    /// <summary>
    /// Increases indentation without emitting a source block.
    /// </summary>
    /// <param name="count">
    /// The nonnegative number of levels to add.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The count is negative.
    /// </exception>
    void Indent(int count = 1);

    /// <summary>
    /// Decreases indentation without emitting a source block.
    /// </summary>
    /// <param name="count">
    /// The nonnegative number of levels to remove.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The count is negative or exceeds the current depth.
    /// </exception>
    void Unindent(int count = 1);

    /// <summary>
    /// Opens a block whose closing marker is emitted when the returned scope is disposed.
    /// </summary>
    /// <param name="declaration">
    /// Source text preceding the opening brace.
    /// </param>
    /// <returns>
    /// An owned disposable scope; disposing it again has no effect.
    /// </returns>
    IDisposable PushBlock(string declaration);

    /// <summary>
    /// Writes a source character at the current indentation depth.
    /// </summary>
    /// <param name="chr">
    /// The character to write.
    /// </param>
    void Write(char chr);

    /// <summary>
    /// Writes a source fragment, preserving its line endings.
    /// </summary>
    /// <param name="text">
    /// The source fragment.
    /// </param>
    void Write(string text);

    /// <summary>
    /// Emits an empty line and prepares indentation for the next source fragment.
    /// </summary>
    void WriteLine();

    /// <summary>
    /// Writes a source fragment followed by the implementation's line terminator.
    /// </summary>
    /// <param name="text">
    /// The source text preceding the terminator.
    /// </param>
    void WriteLine(string text);

    /// <summary>
    /// Emits each line of a source fragment using the implementation's line terminator.
    /// </summary>
    /// <param name="text">
    /// Source text with CR, LF or CRLF separators; null emits nothing.
    /// </param>
    void WriteLines(string? text);

    /// <summary>
    /// Emits source lines in their enumeration order.
    /// </summary>
    /// <param name="lines">
    /// The caller-owned collection of source lines.
    /// </param>
    void WriteLines(IEnumerable<string> lines);
}
