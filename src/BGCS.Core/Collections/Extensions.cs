using System;
using System.Linq;

namespace BGCS.Core.Collections
{
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Copies collection containers and reverses mutable text used during identifier construction.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Reverses UTF-16 code units in the supplied builder in place.
        /// </summary>
        /// <param name="sb">The builder to modify; surrogate pairs are not treated as single characters.</param>
        public static void Reverse(this StringBuilder sb)
        {
            char t;
            int end = sb.Length - 1;
            int start = 0;
            while (end - start > 0)
            {
                t = sb[end];
                sb[end] = sb[start];
                sb[start] = t;
                start++;
                end--;
            }
        }

        /// <summary>
        /// Copies a dictionary container while preserving references to its keys and values.
        /// </summary>
        /// <typeparam name="TKey">The non-null key type.</typeparam>
        /// <typeparam name="TValue">The stored value type.</typeparam>
        /// <param name="dictionary">The dictionary to copy.</param>
        /// <returns>A new dictionary preserving the source key comparer and the same entries.</returns>
        /// <exception cref="ArgumentNullException">The dictionary is null.</exception>
        public static Dictionary<TKey, TValue> Clone<TKey, TValue>(this Dictionary<TKey, TValue> dictionary)
            where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(dictionary);
            return new(dictionary, dictionary.Comparer);
        }

        /// <summary>
        /// Copies a list container while preserving references to its elements.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="list">The list to copy.</param>
        /// <returns>A new list containing the same elements in their original order.</returns>
        public static List<T> Clone<T>(this List<T> list)
        {
            return new(list);
        }

        /// <summary>
        /// Clones each metadata value into a new list without modifying the source list.
        /// </summary>
        /// <typeparam name="T">The metadata type with a typed cloning contract.</typeparam>
        /// <param name="list">The non-null values to clone in enumeration order.</param>
        /// <returns>A new list containing the independent clones.</returns>
        public static List<T> CloneValues<T>(this IList<T> list)
            where T : ICloneable<T>
        {
            return new(list.Select(x => x.Clone()));
        }
    }
}
