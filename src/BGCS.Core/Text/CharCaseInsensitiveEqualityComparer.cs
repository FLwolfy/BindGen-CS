using System.Collections.Generic;

namespace BGCS.Core.Text;

/// <summary>
/// Compares UTF-16 characters using invariant case folding, independent of the current thread culture.
/// </summary>
public sealed class CharCaseInsensitiveEqualityComparer : IEqualityComparer<char>
{
    /// <summary>
    /// The shared stateless character comparer.
    /// </summary>
    public static readonly CharCaseInsensitiveEqualityComparer @default = new();

    /// <inheritdoc />
    public bool Equals(
        char x,
        char y
    ) => char.ToUpperInvariant(x) == char.ToUpperInvariant(y);

    /// <inheritdoc />
    public int GetHashCode(char obj) => char.ToUpperInvariant(obj).GetHashCode();
}
