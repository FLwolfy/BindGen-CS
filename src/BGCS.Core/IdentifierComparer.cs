namespace BGCS.Core
{
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Defines the public class <c>IdentifierComparer</c>.
    /// </summary>
    public class IdentifierComparer<T> : IEqualityComparer<T> where T : class, IHasIdentifier
    {
        /// <summary>
        /// Executes public operation <c>new</c>.
        /// </summary>
        public static readonly IdentifierComparer<T> Default = new();

        /// <summary>
        /// Executes public operation <c>Equals</c>.
        /// </summary>
        public bool Equals(T? x, T? y)
        {
            return x?.Identifier == y?.Identifier;
        }

        /// <summary>
        /// Returns computed data from <c>GetHashCode</c>.
        /// </summary>
        public int GetHashCode([DisallowNull] T obj)
        {
            return obj.Identifier.GetHashCode();
        }
    }
}
