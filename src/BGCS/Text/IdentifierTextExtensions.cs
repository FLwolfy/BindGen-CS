using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.Text;

/// <summary>
/// Splits identifier word boundaries and normalizes name fragments with invariant casing.
/// </summary>
public static class IdentifierTextExtensions
{
    /// <summary>
    /// Capitalizes the first character and characters following digits, lowercasing other characters with invariant rules.
    /// </summary>
    /// <param name="str">
    /// The identifier fragment to normalize; empty fragments remain empty.
    /// </param>
    /// <returns>
    /// A new normalized fragment, or the empty string for empty input.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The fragment is null.
    /// </exception>
    public static string ToTitleCaseFragment(this string str)
    {
        ArgumentNullException.ThrowIfNull(str);
        return string.Create(str.Length, str, static (
            characters,
            source
        ) => {
            bool followsDigit = false;
            for (int index = 0; index < source.Length; index++)
            {
                char character = source[index];
                characters[index] = index == 0 || followsDigit ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character);
                followsDigit = char.IsDigit(character);
            }
        });
    }

    private enum SplitByCaseModes
    {
        None,
        WhiteSpace,
        Digit,
        UpperCase,
        LowerCase
    }

    /// <summary>
    /// Splits an identifier at character-category changes while retaining an uppercase acronym before a title-cased word.
    /// </summary>
    /// <param name="s">
    /// The identifier text to split; whitespace runs are preserved as separate fragments.
    /// </param>
    /// <returns>
    /// Ordered word, acronym, numeric, or whitespace fragments; an empty array for empty input.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The input text is null.
    /// </exception>
    public static string[] SplitByCase(this string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var parts = new List<string>();
        var word = new StringBuilder();
        var previous = SplitByCaseModes.None;
        foreach (var character in s)
        {
            SplitByCaseModes current;
            if (char.IsWhiteSpace(character))
            {
                current = SplitByCaseModes.WhiteSpace;
            }
            else if (char.IsDigit(character))
            {
                current = SplitByCaseModes.Digit;
            }
            else if (character == char.ToUpperInvariant(character))
            {
                current = SplitByCaseModes.UpperCase;
            }
            else
            {
                current = SplitByCaseModes.LowerCase;
            }

            if (previous == SplitByCaseModes.None || previous == current)
            {
                word.Append(character);
            }
            else if (previous == SplitByCaseModes.UpperCase && current == SplitByCaseModes.LowerCase)
            {
                if (word.Length > 1)
                {
                    parts.Add(word.ToString()[..(word.Length - 1)]);
                    word.Remove(0, word.Length - 1);
                }

                word.Append(character);
            }
            else
            {
                parts.Add(word.ToString());
                word.Clear();
                word.Append(character);
            }

            previous = current;
        }

        if (word.Length != 0)
            parts.Add(word.ToString());
        return parts.ToArray();
    }

}
