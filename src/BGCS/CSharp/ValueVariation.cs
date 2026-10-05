using System;
using System.Linq;

namespace BGCS.CSharp
{
    using System.Collections.Generic;

    /// <summary>
    /// Captures a managed method name and ordered parameter projections as a stable overload comparison key.
    /// </summary>
    public readonly struct ValueVariation : IEquatable<ValueVariation>
    {
        private readonly string? m_name;
        private readonly CsParameterInfo[]? m_parameters;
        private readonly int m_hashCode;
        /// <summary>
        /// Copies managed descriptors and captures their conflict hash; native AST references remain owned by the analysis invocation.
        /// </summary>
        /// <param name="name">The exact managed method name.</param>
        /// <param name="parameters">The ordered parameter projections; the container and mutable managed descriptors are not retained.</param>
        /// <exception cref="ArgumentNullException">The name or parameter list is null.</exception>
        public ValueVariation(
            string name,
            IList<CsParameterInfo> parameters
        ) {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(parameters);
            m_name = name;
            m_parameters = parameters.Select(parameter => parameter.Clone()).ToArray();
            HashCode hash = new();
            hash.Add(m_name, StringComparer.Ordinal);
            foreach (CsParameterInfo parameter in m_parameters)
            {
                hash.Add(parameter.type.GetConflictHashCode());
                hash.Add(parameter.defaultValue, StringComparer.Ordinal);
            }

            m_hashCode = hash.ToHashCode();
        }

        /// <summary>
        /// Gets the captured managed method name, or an empty string for an uninitialized value.
        /// </summary>
        public string name => m_name ?? string.Empty;
        /// <summary>
        /// Gets independently mutable copies of the captured parameters; changing them cannot corrupt a signature set.
        /// </summary>
        public IReadOnlyList<CsParameterInfo> parameters => m_parameters == null
            ? Array.Empty<CsParameterInfo>()
            : m_parameters.Select(parameter => parameter.Clone()).ToArray();

        /// <inheritdoc />
        public override readonly bool Equals(object? obj)
        {
            return obj is ValueVariation variation && Equals(variation);
        }

        /// <summary>
        /// Compares captured names and ordered parameter conflicts, including default expressions.
        /// </summary>
        /// <param name="other">The captured signature to compare.</param>
        /// <returns>True for equivalent captured signatures; false for different names, arity, types, or defaults.</returns>
        public readonly bool Equals(ValueVariation other)
        {
            if (other.m_name != this.m_name)
                return false;
            int count = m_parameters?.Length ?? 0;
            if (count != (other.m_parameters?.Length ?? 0))
                return false;
            for (int i = 0; i < count; i++)
            {
                if (!other.m_parameters![i].Conflicts(this.m_parameters![i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns the conflict hash computed when the signature was captured.
        /// </summary>
        /// <returns>The in-process hash, or zero for an uninitialized value; it is not a persistent identity.</returns>
        public override readonly int GetHashCode()
        {
            return m_hashCode;
        }

        /// <summary>
        /// Compares two captured signatures for equivalence.
        /// </summary>
        /// <param name="left">The first captured signature.</param>
        /// <param name="right">The second captured signature.</param>
        /// <returns>True when the signatures are equivalent; otherwise false.</returns>
        public static bool operator ==(
            ValueVariation left,
            ValueVariation right
        ) {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two captured signatures for a difference.
        /// </summary>
        /// <param name="left">The first captured signature.</param>
        /// <param name="right">The second captured signature.</param>
        /// <returns>True when the signatures differ; otherwise false.</returns>
        public static bool operator !=(
            ValueVariation left,
            ValueVariation right
        ) {
            return !(left == right);
        }
    }
}
