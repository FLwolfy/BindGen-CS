namespace BGCS.Core.Collections
{
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>CollectionHelper</c>.
    /// </summary>
    public static class CollectionHelper
    {
        /// <summary>
        /// Adds data or behavior through <c>AddRange</c>.
        /// </summary>
        public static void AddRange<T>(this HashSet<T> destination, IEnumerable<T> values)
        {
            foreach (var value in values)
            {
                destination.Add(value);
            }
        }

        /// <summary>
        /// Adds data or behavior through <c>AddRange</c>.
        /// </summary>
        public static void AddRange<TKey, TValue>(this Dictionary<TKey, TValue> destination, Dictionary<TKey, TValue> values) where TKey : notnull
        {
            foreach (var pair in values)
            {
                destination[pair.Key] = pair.Value;
            }
        }

        /// <summary>
        /// Returns computed data from <c>Get</c>.
        /// </summary>
        public static T? Get<T>(this List<T>? list, int index)
        {
            if (list == null)
            {
                return default;
            }
            return list[index];
        }
    }
}
