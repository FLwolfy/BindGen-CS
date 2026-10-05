using System.Collections.Generic;

namespace BGCS.Core.Collections
{
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Compares metadata objects by their ordinal semantic identifiers rather than their references.
    /// </summary>
    /// <typeparam name="T">The metadata type supplying an identifier.</typeparam>
    public class IdentifierComparer<T> : IEqualityComparer<T> where T : class, IHasIdentifier
    {
        /// <summary>
        /// Gets the shared stateless comparer for this metadata type.
        /// </summary>
        public static readonly IdentifierComparer<T> @default = new();
        /// <summary>
        /// Compares the identifiers of two metadata objects, treating two null objects as equal.
        /// </summary>
        /// <param name="x">The first metadata object, or null.</param>
        /// <param name="y">The second metadata object, or null.</param>
        /// <returns>True when both identifiers are equal; otherwise false.</returns>
        public bool Equals(
            T? x,
            T? y
        ) {
            return x?.identifier == y?.identifier;
        }

        /// <summary>
        /// Computes the ordinal hash of the object's identifier.
        /// </summary>
        /// <param name="obj">The non-null metadata object whose identifier is hashed.</param>
        /// <returns>The hash of the semantic identifier.</returns>
        public int GetHashCode([DisallowNull] T obj)
        {
            return obj.identifier.GetHashCode();
        }
    }
}
