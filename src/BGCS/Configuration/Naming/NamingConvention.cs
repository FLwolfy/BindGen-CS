using System;
using System.Collections.Generic;
using BGCS.Core.Text;
using BGCS.Text;

namespace BGCS.Configuration.Naming
{
    using System.Text;

    /// <summary>
    /// Selects managed word casing and separators or preserves an unknown source spelling.
    /// </summary>
    public enum NamingConvention
    {
        /// <summary>
        /// No convention was detected, or the original spelling should be preserved.
        /// </summary>
        Unknown,
        /// <summary>
        /// Words joined with an uppercase initial for each word.
        /// </summary>
        PascalCase,
        /// <summary>
        /// Words joined with a lowercase first initial and uppercase later initials.
        /// </summary>
        CamelCase,
        /// <summary>
        /// Lowercase words separated by underscores.
        /// </summary>
        SnakeCase,
        /// <summary>
        /// Uppercase words separated by underscores.
        /// </summary>
        ScreamingSnakeCase,
        /// <summary>
        /// Uppercase text without word separators.
        /// </summary>
        UpperFlatCase,
        /// <summary>
        /// Lowercase text without word separators.
        /// </summary>
        LowerFlatCase,
    }

    /// <summary>
    /// Splits native identifiers into words and converts their managed presentation using invariant casing.
    /// </summary>
    public static class NamingHelper
    {
        /// <summary>
        /// Classifies identifier spelling from underscores and upper/lowercase characters.
        /// </summary>
        /// <param name="input">
        /// The candidate identifier; null or empty has no detectable convention.
        /// </param>
        /// <returns>
        /// The detected casing category, or Unknown when no category can be inferred.
        /// </returns>
        public static NamingConvention AnalyzeNamingConvention(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return NamingConvention.Unknown;
            }

            bool hasUnderscore = input.Contains('_');
            bool hasLowerCase = char.IsLower(input[0]);
            bool hasUpperCase = false;
            bool firstIsUpperCase = char.IsUpper(input[0]);
            hasUpperCase |= firstIsUpperCase;
            for (int i = 1; i < input.Length; i++)
            {
                var c = input[i];
                if (char.IsLower(c))
                {
                    hasLowerCase = true;
                }
                else if (char.IsUpper(c))
                {
                    hasUpperCase = true;
                }

                if (hasUpperCase && hasLowerCase)
                {
                    break;
                }
            }

            if (hasUnderscore)
            {
                if (!hasLowerCase && hasUpperCase)
                {
                    return NamingConvention.ScreamingSnakeCase;
                }

                return NamingConvention.SnakeCase;
            }
            else if (firstIsUpperCase && hasLowerCase && hasUpperCase)
            {
                return NamingConvention.PascalCase;
            }
            else if (hasLowerCase)
            {
                return NamingConvention.CamelCase;
            }
            else if (hasUpperCase)
            {
                return NamingConvention.UpperFlatCase;
            }

            return NamingConvention.Unknown;
        }

        /// <summary>
        /// Splits an identifier using its known spelling convention or lossless English word segmentation.
        /// </summary>
        /// <param name="input">
        /// The identifier to split into words.
        /// </param>
        /// <param name="convention">
        /// The source casing policy controlling case, underscore, or dictionary splitting.
        /// </param>
        /// <returns>
        /// A newly allocated ordered word array retaining numeric and unknown fragments;
        /// underscore splitting may retain empty segments.
        /// </returns>
        public static string[] GetParts(
            string input,
            NamingConvention convention
        ) {
            string[] parts = new string[]
            {
                input
            };
            switch (convention)
            {
                case NamingConvention.CamelCase:
                case NamingConvention.PascalCase:
                    parts = input.SplitByCase();
                    break;
                case NamingConvention.ScreamingSnakeCase:
                case NamingConvention.SnakeCase:
                    parts = input.Split("_");
                    break;
                case NamingConvention.Unknown:
                case NamingConvention.LowerFlatCase:
                case NamingConvention.UpperFlatCase:
                    parts = SplitFlatIdentifier(input);
                    break;
            }

            return parts;
        }

        /// <summary>
        /// Converts identifier words to the requested invariant casing and separator policy.
        /// </summary>
        /// <param name="input">
        /// The identifier to convert.
        /// </param>
        /// <param name="targetConvention">
        /// The desired managed spelling; Unknown preserves the input.
        /// </param>
        /// <returns>
        /// The converted identifier, or the original text when its detected convention already matches the target.
        /// </returns>
        public static string ConvertTo(
            string input,
            NamingConvention targetConvention
        ) {
            // unknown as target is basically keep original convention.
            if (targetConvention == NamingConvention.Unknown)
            {
                return input;
            }

            var sourceConvention = AnalyzeNamingConvention(input);
            if (sourceConvention == targetConvention)
            {
                return input;
            }

            var parts = GetParts(input, sourceConvention);
            StringBuilder sb = new();
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                if (char.IsDigit(part[0]) || char.IsDigit(part[^1]))
                {
                    sb.Append(part);
                    continue;
                }

                switch (targetConvention)
                {
                    case NamingConvention.CamelCase:
                        if (i == 0)
                        {
                            bool wasNumber = false;
                            for (int j = 0; j < part.Length; j++)
                            {
                                var c = part[j];
                                if (wasNumber)
                                {
                                    c = char.ToUpperInvariant(c);
                                }
                                else
                                {
                                    c = char.ToLowerInvariant(c);
                                }

                                sb.Append(c);
                                wasNumber = char.IsDigit(c);
                            }
                        }
                        else
                        {
                            sb.Append(part.Capitalize());
                        }

                        break;
                    case NamingConvention.PascalCase:
                        {
                            bool wasNumber = false;
                            for (int j = 0; j < part.Length; j++)
                            {
                                var c = part[j];
                                if (wasNumber || j == 0)
                                {
                                    c = char.ToUpperInvariant(c);
                                }
                                else
                                {
                                    c = char.ToLowerInvariant(c);
                                }

                                sb.Append(c);
                                wasNumber = char.IsDigit(c);
                            }
                        }

                        break;
                    case NamingConvention.SnakeCase:
                        if (i > 0)
                            sb.Append('_');
                        sb.Append(part.ToLowerInvariant());
                        break;
                    case NamingConvention.ScreamingSnakeCase:
                        if (i > 0)
                            sb.Append('_');
                        sb.Append(part.ToUpperInvariant());
                        break;
                    case NamingConvention.UpperFlatCase:
                        sb.Append(part.ToUpperInvariant());
                        break;
                    case NamingConvention.LowerFlatCase:
                        sb.Append(part.ToLowerInvariant());
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Uppercases the first character and lowercases remaining characters using invariant casing.
        /// </summary>
        /// <param name="input">
        /// The fragment to capitalize; an empty fragment remains empty.
        /// </param>
        /// <returns>
        /// A separately encoded fragment, or an empty string for empty input.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The input fragment is null.
        /// </exception>
        public static string Capitalize(this string input)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (input.Length == 0)
                return string.Empty;
            return string.Create(input.Length, input, (
                characters,
                source
            ) => {
                characters[0] = char.ToUpperInvariant(source[0]);
                for (int index = 1; index < characters.Length; index++)
                    characters[index] = char.ToLowerInvariant(source[index]);
            });
        }

        private static string[] SplitFlatIdentifier(string input)
        {
            var parts = new List<string>();
            int offset = 0;
            while (offset < input.Length)
            {
                int length = WordList.en_EN.FindMostMatching(input.AsSpan(offset)).Length;
                if (length == 0)
                {
                    bool numeric = char.IsDigit(input[offset]);
                    length = 1;
                    while (offset + length < input.Length
                        && char.IsDigit(input[offset + length]) == numeric
                        && WordList.en_EN.FindMostMatching(input.AsSpan(offset + length)).IsEmpty)
                    {
                        length++;
                    }
                }
                parts.Add(input.Substring(offset, length));
                offset += length;
            }
            return parts.ToArray();
        }
    }
}
