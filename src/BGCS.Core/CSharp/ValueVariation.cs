namespace BGCS.Core.CSharp
{
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public struct <c>ValueVariation</c>.
    /// </summary>
    public readonly struct ValueVariation : IEquatable<ValueVariation>
    {
        private readonly string name;
        private readonly IList<CsParameterInfo> parameters;

        /// <summary>
        /// Initializes a new instance of <see cref="ValueVariation"/>.
        /// </summary>
        public ValueVariation(string name, IList<CsParameterInfo> parameters)
        {
            this.name = name;
            this.parameters = parameters;
        }

        /// <summary>
        /// Exposes public member <c>name</c>.
        /// </summary>
        public readonly string Name => name;

        /// <summary>
        /// Exposes public member <c>parameters</c>.
        /// </summary>
        public readonly IList<CsParameterInfo> Parameters => parameters;

        /// <summary>
        /// Executes public operation <c>Equals</c>.
        /// </summary>
        public override readonly bool Equals(object? obj)
        {
            return obj is ValueVariation variation && Equals(variation);
        }

        /// <summary>
        /// Executes public operation <c>Equals</c>.
        /// </summary>
        public readonly bool Equals(ValueVariation other)
        {
            if (other.parameters.Count != parameters.Count) return false;
            if (other.name != name) return false;
            for (int i = 0; i < parameters.Count; i++)
            {
                if (!other.parameters[i].Conflicts(parameters[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns computed data from <c>GetHashCode</c>.
        /// </summary>
        public override readonly int GetHashCode()
        {
            HashCode code = new();
            code.Add(name);
            foreach (var parameter in parameters)
            {
                code.Add(parameter.Type.GetConflictHashCode());
            }
            return code.ToHashCode();
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator ==(ValueVariation left, ValueVariation right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator !=(ValueVariation left, ValueVariation right)
        {
            return !(left == right);
        }
    }
}
