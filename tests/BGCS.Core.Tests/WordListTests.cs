using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using BGCS.Core.Collections;
using BGCS.Core.Text;
using Xunit;

namespace BGCS.Core.Tests;

#pragma warning disable xUnit2017 // Contains uses the tree's configured character comparer, not string equality.

public sealed class WordListTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "BgcsWordListTests", Guid.NewGuid().ToString("N"));

    public WordListTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void TextAndBinaryWrites_RoundTripCompleteWordsAndRetainBorrowedStreams()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("alpha\nALPHA\na\nβeta\n"));
        var words = new WordList();
        words.ReadTxt(input);
        Assert.True(input.CanRead);
        string binary = Path.Combine(m_root, "words.bin");
        string text = Path.Combine(m_root, "words.txt");
        words.Write(binary);
        words.WriteTxt(text);

        Assert.Equal("a\nalpha\nβeta\n", File.ReadAllText(text));
        Assert.Equal("alpha", WordList.ReadFrom(binary).FindMostMatching("alphabets").ToString());
        Assert.Equal("βeta", WordList.ReadFromTxt(text).FindMostMatching("βetaValue").ToString());
        Assert.Equal(new[] { "alpha", "βeta" }, words.SplitWords("_alpha#βeta!"));
        Assert.Empty(Directory.EnumerateFiles(m_root, "*.staging-*"));
    }

    [Fact]
    public void CreateFromText_UsesTheSameCompleteBinaryWriter()
    {
        string source = Path.Combine(m_root, "source.txt");
        string destination = Path.Combine(m_root, "output.bin");
        File.WriteAllText(source, "a\nalpha\nalpha\n");
        WordList.CreateFromTxt(source, destination);

        Assert.Equal("a", WordList.ReadFrom(destination).FindMostMatching("a").ToString());
        Assert.Throws<ArgumentException>(() => WordList.CreateFromTxt(source, source));
    }

    [Fact]
    public void CaseEquality_RemainsStableAcrossCultureChanges()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var keys = new TrieStringSet(CharCaseInsensitiveEqualityComparer.@default);
            keys.Add("INPUT");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            Assert.True(keys.Contains("input"));
            Assert.Equal(CharCaseInsensitiveEqualityComparer.@default.GetHashCode('I'),
                CharCaseInsensitiveEqualityComparer.@default.GetHashCode('i'));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 3)]
    [InlineData(1, 1024 * 1024 + 2)]
    public void InvalidBinaryInput_IsRejectedWithoutChangingTheVocabulary(
        int count,
        int length
    ) {
        var words = new WordList();
        using var input = new MemoryStream("alpha\n"u8.ToArray());
        words.ReadTxt(input);
        string invalid = Path.Combine(m_root, "invalid.bin");
        using (FileStream file = File.Create(invalid))
        {
            Span<byte> header = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, count);
            file.Write(header);
            using var compressed = new DeflateStream(file, CompressionLevel.Optimal);
            BinaryPrimitives.WriteInt32LittleEndian(header, length);
            compressed.Write(header);
        }

        Assert.Throws<InvalidDataException>(() => words.Read(invalid));
        Assert.Equal("alpha", words.FindMostMatching("alphabets").ToString());
    }

    public void Dispose() => Directory.Delete(m_root, recursive: true);
}
