using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BGCS.Contracts.Managed;

namespace BGCS.CSharp.Projection;

internal sealed class FlatIdentifierWordIndex
{
    private const string C_ENGLISH_DIGEST = "480a91457cb2e2e11226f0f37d91e8bfce2d9c02825e02a0b8d2de2a70a45bcc";
    private static readonly Lazy<FlatIdentifierWordIndex> s_english = new(ReadEnglish);
    private readonly FrozenSet<string> m_words;
    private readonly ImmutableArray<int> m_lengths;

    private FlatIdentifierWordIndex(IEnumerable<string> words)
    {
        m_words = words.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        m_lengths = m_words.Select(word => word.Length).Distinct().OrderDescending().ToImmutableArray();
    }

    internal static FlatIdentifierWordIndex? Create(ManagedNamingPolicy policy) => policy.flatIdentifierSegmentation switch
    {
        ManagedFlatIdentifierSegmentation.None => null,
        ManagedFlatIdentifierSegmentation.English => s_english.Value,
        ManagedFlatIdentifierSegmentation.Custom => new(policy.flatIdentifierWords),
        _ => throw new ArgumentException("The flat identifier segmentation mode is undefined.")
    };

    internal void AppendWords(
        string word,
        List<string> output
    ) {
        // Lowercase, Pascal and Camel source words retain their existing case-boundary semantics.
        if (!word.Any(char.IsUpper) || word.Any(char.IsLower)) { output.Add(word); return; }
        int offset = 0;
        while (offset < word.Length)
        {
            int length = Match(word.AsSpan(offset));
            if (length == 0)
            {
                bool numeric = char.IsDigit(word[offset]);
                length = 1;
                while (offset + length < word.Length && char.IsDigit(word[offset + length]) == numeric
                    && Match(word.AsSpan(offset + length)) == 0) length++;
            }
            output.Add(word.Substring(offset, length));
            offset += length;
        }
    }

    private int Match(ReadOnlySpan<char> remaining)
    {
        var lookup = m_words.GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (int length in m_lengths)
            if (length <= remaining.Length && lookup.Contains(remaining[..length])) return length;
        return 0;
    }

    private static FlatIdentifierWordIndex ReadEnglish()
    {
        using Stream resource = typeof(FlatIdentifierWordIndex).Assembly.GetManifestResourceStream("BGCS.CSharp.Naming.en_EN.txt")
            ?? throw new InvalidDataException("The versioned English naming vocabulary is missing.");
        using MemoryStream image = new();
        resource.CopyTo(image);
        if (Convert.ToHexStringLower(SHA256.HashData(image.GetBuffer().AsSpan(0, checked((int)image.Length)))) != C_ENGLISH_DIGEST)
            throw new InvalidDataException("The versioned English naming vocabulary has an unexpected content digest.");
        image.Position = 0;
        using StreamReader reader = new(image, new UTF8Encoding(false, true));
        List<string> words = [];
        while (reader.ReadLine() is { } word)
            if (word.Length != 0) words.Add(word);
        return new(words);
    }
}
