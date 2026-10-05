using BGCS.Core.Collections;
using BGCS.Emission;
namespace BGCS.Metadata
{
    using Newtonsoft.Json;

    /// <summary>
    /// Retains a native constant spelling and its mutable managed emission projection.
    /// </summary>
    public class CsConstantMetadata : IHasIdentifier, ICloneable<CsConstantMetadata>
    {
        /// <summary>
        /// Retains native constant spelling and any already mapped managed projection.
        /// </summary>
        /// <param name="cppName">
        /// The original native constant identifier.
        /// </param>
        /// <param name="cppValue">
        /// The original native expression spelling.
        /// </param>
        /// <param name="type">
        /// The selected constant carrier category.
        /// </param>
        public CsConstantMetadata(
            string cppName,
            string cppValue,
            CsConstantType type
        ) {
            this.cppName = cppName;
            this.cppValue = cppValue;
            this.type = type;
        }

        /// <summary>
        /// Retains native constant spelling and any already mapped managed projection.
        /// </summary>
        /// <param name="cppName">
        /// The original native constant identifier.
        /// </param>
        /// <param name="cppValue">
        /// The original native expression spelling.
        /// </param>
        /// <param name="name">
        /// The managed identifier, or null before mapping.
        /// </param>
        /// <param name="value">
        /// The managed expression spelling, or null before mapping.
        /// </param>
        /// <param name="type">
        /// The selected constant carrier category.
        /// </param>
        /// <param name="comment">
        /// The emitted documentation fragment, or null when absent.
        /// </param>
        [JsonConstructor]
        public CsConstantMetadata(
            string cppName,
            string cppValue,
            string? name,
            string? value,
            CsConstantType type,
            string? comment
        ) {
            this.cppName = cppName;
            this.cppValue = cppValue;
            this.name = name;
            this.value = value;
            this.type = type;
            this.comment = comment;
        }

        /// <summary>
        /// Gets the original native identifier used for metadata matching.
        /// </summary>
        public string identifier => this.cppName;
        /// <summary>
        /// Gets or sets the original native constant identifier.
        /// </summary>
        public string cppName { get; set; }
        /// <summary>
        /// Gets or sets the original native expression spelling.
        /// </summary>
        public string cppValue { get; set; }
        /// <summary>
        /// Formats the original native expression as an escaped C# string literal.
        /// </summary>
        public string escapedCppValue => this.cppValue.ToLiteral();
        /// <summary>
        /// Gets or sets the managed identifier, or null before identifier mapping.
        /// </summary>
        public string? name { get; set; }
        /// <summary>
        /// Gets or sets the managed expression spelling, or null before expression mapping.
        /// </summary>
        public string? value { get; set; }
        /// <summary>
        /// Gets or sets the selected managed constant carrier category.
        /// </summary>
        public CsConstantType type { get; set; }
        /// <summary>
        /// Gets or sets an explicit carrier type override, or null to use the selected constant category.
        /// </summary>
        public string? customType { get; set; }
        /// <summary>
        /// Gets or sets the emitted documentation fragment, or null when absent.
        /// </summary>
        public string? comment { get; set; }

        /// <summary>
        /// Hashes the original native identifier for in-process metadata lookup.
        /// </summary>
        /// <returns>
        /// The current identifier hash; changing cppName changes this value.
        /// </returns>
        public override int GetHashCode()
        {
            return this.cppName.GetHashCode();
        }

        /// <summary>
        /// Formats the native identifier and expression for diagnostic display.
        /// </summary>
        /// <returns>
        /// The native constant declaration summary.
        /// </returns>
        public override string ToString()
        {
            return $"Constant: {this.cppName} = {this.cppValue}";
        }

        /// <summary>
        /// Copies the native and managed spellings, carrier override, and documentation into an independent descriptor.
        /// </summary>
        /// <returns>
        /// A separately mutable constant projection retaining the same immutable text values.
        /// </returns>
        public CsConstantMetadata Clone()
        {
            return new CsConstantMetadata(this.cppName, this.cppValue, this.name, this.value, this.type, this.comment) { customType = customType };
        }
    }
}
