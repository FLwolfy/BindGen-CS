using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.Core.Text;

/// <summary>
/// Produces portable generated file names while preserving an optional directory prefix verbatim.
/// </summary>
public static class FileNameHelper
{
    private const int C_MAX_FILE_NAME_UNITS = 255;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Dictionary<char, string> Replacements = new()
    {
        ['*'] = "Star",
        [':'] = "Colon",
        ['<'] = "LessThan",
        ['>'] = "GreaterThan",
        ['|'] = "Pipe",
        ['?'] = "QuestionMark",
        ['"'] = "Quote"
    };
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³"
    };

    /// <summary>
    /// Replaces reserved characters in the leaf name, prefixes Windows device names, and removes trailing dots and spaces.
    /// The leaf is limited to 255 UTF-16 units and 255 UTF-8 bytes without splitting a Unicode scalar.
    /// Both slash styles are recognized on every host; directory components are not sanitized.
    /// </summary>
    /// <param name="fileName">A leaf name or a path ending in a leaf name.</param>
    /// <returns>The preserved directory prefix followed by a non-empty, portable leaf name.</returns>
    /// <exception cref="ArgumentException">The path is blank, ends in a separator, or contains malformed UTF-16.</exception>
    public static string SanitizeFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        int separator = Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\'));
        string directory = separator < 0 ? string.Empty : fileName[..(separator + 1)];
        string leaf = fileName[(separator + 1)..];
        if (leaf.Length == 0)
            throw new ArgumentException("A generated path must end in a file name.", nameof(fileName));
        try
        {
            Utf8.GetByteCount(leaf);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("A generated file name must contain valid Unicode scalars.", nameof(fileName), exception);
        }
        var output = new StringBuilder(leaf.Length);
        foreach (char character in leaf)
        {
            if (Replacements.TryGetValue(character, out string? replacement))
                output.Append(replacement);
            else
                output.Append(character < ' ' ? '_' : character);
        }
        leaf = output.ToString().TrimEnd(' ', '.');
        if (leaf.Length == 0)
            leaf = "_";
        int extension = leaf.IndexOf('.');
        string stem = (extension < 0 ? leaf : leaf[..extension]).TrimEnd(' ');
        if (ReservedNames.Contains(stem))
            leaf = "_" + leaf;

        int byteCount = 0;
        int unitCount = 0;
        foreach (Rune character in leaf.EnumerateRunes())
        {
            if (byteCount + character.Utf8SequenceLength > C_MAX_FILE_NAME_UNITS
                || unitCount + character.Utf16SequenceLength > C_MAX_FILE_NAME_UNITS)
                break;
            byteCount += character.Utf8SequenceLength;
            unitCount += character.Utf16SequenceLength;
        }
        return directory + leaf[..unitCount].TrimEnd(' ', '.');
    }
}
