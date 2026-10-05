using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using BGCS.Core.Collections;

namespace BGCS.Core.Text;

/// <summary>
/// Maintains an invariant, case-insensitive vocabulary for splitting native identifiers.
/// File reads merge complete validated input; callers retain ownership of supplied streams.
/// </summary>
public class WordList
{
    private const int C_MAX_WORD_BYTES = 1024 * 1024;
    private static readonly UnicodeEncoding BinaryEncoding = new(false, false, true);
    private static readonly UTF8Encoding TextEncoding = new(false, true);

    /// <summary>
    /// The shared English vocabulary loaded from the embedded resource.
    /// </summary>
    public static readonly WordList en_EN = new("en_EN");

    private readonly TrieStringSet m_words = new(CharCaseInsensitiveEqualityComparer.@default);

    /// <summary>
    /// Creates an empty vocabulary.
    /// </summary>
    public WordList() { }

    /// <summary>
    /// Loads an embedded vocabulary, or a text file named after the supplied code when no resource exists.
    /// </summary>
    /// <param name="countryCode">The resource stem or file stem without the .txt suffix.</param>
    /// <exception cref="ArgumentException">The code is empty or whitespace.</exception>
    /// <exception cref="IOException">The requested file cannot be read.</exception>
    public WordList(string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        using Stream? resource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("BGCS.Core.Resources." + countryCode + ".txt");
        if (resource is null)
            ReadTxt(countryCode + ".txt");
        else
            ReadTxt(resource);
    }

    /// <summary>
    /// Reads a text vocabulary and writes its complete compressed representation.
    /// Duplicate words are folded using invariant character equality.
    /// </summary>
    /// <param name="source">The UTF-8 input file.</param>
    /// <param name="destination">The binary output file, distinct from the source.</param>
    /// <exception cref="ArgumentException">A path is blank or both paths identify the same file.</exception>
    /// <exception cref="IOException">Reading or writing fails.</exception>
    public static void CreateFromTxt(
        string source,
        string destination
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("The vocabulary source and destination must be distinct.", nameof(destination));
        ReadFromTxt(source).Write(destination);
    }

    /// <summary>
    /// Merges a compressed vocabulary after its count, UTF-16 lengths and entire payload have been validated.
    /// The format is a little-endian count followed by deflated length-prefixed UTF-16 words.
    /// </summary>
    /// <param name="path">The binary vocabulary file.</param>
    /// <exception cref="ArgumentException">The path is blank.</exception>
    /// <exception cref="InvalidDataException">The count, lengths, encoding or trailing payload is invalid.</exception>
    /// <exception cref="EndOfStreamException">A count, length or word is truncated.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public void Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream file = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        file.ReadExactly(header);
        int count = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (count < 0)
            throw new InvalidDataException("The vocabulary count cannot be negative.");
        using var compressed = new DeflateStream(file, CompressionMode.Decompress);
        var words = new List<string>();
        for (int index = 0; index < count; index++)
        {
            compressed.ReadExactly(header);
            int size = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (size < 0 || size > C_MAX_WORD_BYTES || (size & 1) != 0)
                throw new InvalidDataException($"Invalid UTF-16 word byte length: {size}.");
            byte[] buffer = GC.AllocateUninitializedArray<byte>(size);
            compressed.ReadExactly(buffer);
            try
            {
                words.Add(BinaryEncoding.GetString(buffer));
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("The vocabulary contains malformed UTF-16.", exception);
            }
        }
        if (compressed.ReadByte() != -1)
            throw new InvalidDataException("The vocabulary has data beyond its declared word count.");
        Merge(words);
    }

    /// <summary>
    /// Merges complete UTF-8 text input, treating each line as one word and folding duplicates.
    /// </summary>
    /// <param name="path">The UTF-8 vocabulary file.</param>
    /// <exception cref="ArgumentException">The path is blank.</exception>
    /// <exception cref="DecoderFallbackException">The text contains malformed UTF-8.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public void ReadTxt(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream file = File.OpenRead(path);
        ReadTxt(file);
    }

    /// <summary>
    /// Reads UTF-8 lines from the current stream position, leaving the caller's stream open.
    /// Input is merged only after reading succeeds.
    /// </summary>
    /// <param name="stream">The borrowed readable stream.</param>
    /// <exception cref="ArgumentNullException">The stream is null.</exception>
    /// <exception cref="ArgumentException">The stream cannot be read.</exception>
    /// <exception cref="DecoderFallbackException">The text contains malformed UTF-8.</exception>
    /// <exception cref="IOException">Reading fails.</exception>
    public void ReadTxt(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, TextEncoding, true, 4096, leaveOpen: true);
        var words = new List<string>();
        while (reader.ReadLine() is { } word)
            words.Add(word);
        Merge(words);
    }

    /// <summary>
    /// Writes all stored words in ordinal order as a compressed binary vocabulary.
    /// </summary>
    /// <param name="path">The output file, replaced only after writing succeeds.</param>
    /// <exception cref="ArgumentException">The path is blank.</exception>
    /// <exception cref="InvalidDataException">A word exceeds one MiB of UTF-16 bytes.</exception>
    /// <exception cref="IOException">Writing or committing the file fails.</exception>
    public void Write(string path)
    {
        string[] words = m_words.OrderBy(static word => word, StringComparer.Ordinal).ToArray();
        WriteCompleteFile(path, file =>
        {
            Span<byte> header = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, words.Length);
            file.Write(header);
            using var compressed = new DeflateStream(file, CompressionLevel.Optimal, leaveOpen: true);
            foreach (string word in words)
            {
                byte[] bytes = BinaryEncoding.GetBytes(word);
                if (bytes.Length > C_MAX_WORD_BYTES)
                    throw new InvalidDataException("A vocabulary word exceeds one MiB of UTF-16 bytes.");
                BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
                compressed.Write(header);
                compressed.Write(bytes);
            }
        });
    }

    /// <summary>
    /// Writes stored words in ordinal order as UTF-8 text without a byte-order mark.
    /// </summary>
    /// <param name="path">The output file, replaced only after writing succeeds.</param>
    /// <exception cref="ArgumentException">The path is blank.</exception>
    /// <exception cref="IOException">Writing or committing the file fails.</exception>
    public void WriteTxt(string path)
    {
        WriteCompleteFile(path, file =>
        {
            using var writer = new StreamWriter(file, TextEncoding, 4096, leaveOpen: true);
            writer.NewLine = "\n";
            foreach (string word in m_words.OrderBy(static word => word, StringComparer.Ordinal))
                writer.WriteLine(word);
        });
    }

    /// <summary>
    /// Creates a vocabulary from one compressed binary file.
    /// </summary>
    /// <param name="path">The binary input file.</param>
    /// <returns>The fully loaded vocabulary; malformed or unreadable input throws.</returns>
    public static WordList ReadFrom(string path)
    {
        var words = new WordList();
        words.Read(path);
        return words;
    }

    /// <summary>
    /// Creates a vocabulary from one UTF-8 text file.
    /// </summary>
    /// <param name="path">The text input file.</param>
    /// <returns>The fully loaded vocabulary; malformed or unreadable input throws.</returns>
    public static WordList ReadFromTxt(string path)
    {
        var words = new WordList();
        words.ReadTxt(path);
        return words;
    }

    /// <summary>
    /// Finds the longest stored prefix using invariant, case-insensitive character equality.
    /// </summary>
    /// <param name="text">The span whose prefixes are tested.</param>
    /// <returns>A slice retaining the input's casing, or an empty slice when no non-empty word matches.</returns>
    public ReadOnlySpan<char> FindMostMatching(ReadOnlySpan<char> text) => m_words.FindLargestMatch(text);

    /// <summary>
    /// Splits an identifier greedily into known words, skipping unmatched characters.
    /// </summary>
    /// <param name="text">The identifier whose original casing is preserved in the returned words.</param>
    /// <returns>The matched words in input order, or an empty array when none match.</returns>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
    public string[] SplitWords(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = new List<string>();
        int index = 0;
        while (index < text.Length)
        {
            ReadOnlySpan<char> word = m_words.FindLargestMatch(text.AsSpan(index));
            if (word.IsEmpty)
                index++;
            else
            {
                words.Add(word.ToString());
                index += word.Length;
            }
        }
        return words.ToArray();
    }

    private void Merge(IEnumerable<string> words)
    {
        foreach (string word in words)
        {
            if (!m_words.Contains(word))
                m_words.Add(word);
        }
    }

    private static void WriteCompleteFile(
        string path,
        Action<FileStream> write
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string destination = Path.GetFullPath(path);
        string candidate = destination + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(file);
                file.Flush(flushToDisk: true);
            }
            File.Move(candidate, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(candidate))
                File.Delete(candidate);
        }
    }
}
