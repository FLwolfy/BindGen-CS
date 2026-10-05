using System.Collections.Generic;
using BGCS.Core.Collections;

namespace BGCS.Metadata
{
    using Newtonsoft.Json;

    /// <summary>
    /// Retains one native enum constant and its mutable managed name, expression, attributes, and documentation.
    /// </summary>
    public class CsEnumItemMetadata : IHasIdentifier
    {
        /// <summary>
        /// Retains native enum-item spelling and any already mapped managed projection.
        /// </summary>
        /// <param name="cppName">
        /// The original native enum-item identifier.
        /// </param>
        /// <param name="cppValue">
        /// The original native expression spelling.
        /// </param>
        public CsEnumItemMetadata(
            string cppName,
            string cppValue
        ) {
            this.cppName = cppName;
            this.cppValue = cppValue;
            this.attributes = new();
        }

        /// <summary>
        /// Retains native enum-item spelling and any already mapped managed projection.
        /// </summary>
        /// <param name="cppName">
        /// The original native enum-item identifier.
        /// </param>
        /// <param name="cppValue">
        /// The original native expression spelling.
        /// </param>
        /// <param name="name">
        /// The managed item identifier, or null before mapping.
        /// </param>
        /// <param name="value">
        /// The managed constant expression, or null before mapping.
        /// </param>
        /// <param name="attributes">
        /// Managed attribute fragments retained without copying; null creates an empty list.
        /// </param>
        /// <param name="comment">
        /// The emitted documentation fragment, or null when absent.
        /// </param>
        [JsonConstructor]
        public CsEnumItemMetadata(
            string cppName,
            string cppValue,
            string? name,
            string? value,
            List<string>? attributes,
            string? comment
        ) {
            this.cppName = cppName;
            this.cppValue = cppValue;
            this.name = name;
            this.value = value;
            this.attributes = attributes ?? [];
            this.comment = comment;
        }

        /// <summary>
        /// Gets the original native enum-item identifier used for metadata matching.
        /// </summary>
        public string identifier => this.cppName;
        /// <summary>
        /// Gets or sets the original native enum-item identifier.
        /// </summary>
        public string cppName { get; set; }
        /// <summary>
        /// Gets or sets the original native constant expression spelling.
        /// </summary>
        public string cppValue { get; set; }
        /// <summary>
        /// Gets or sets the managed item identifier, or null before mapping.
        /// </summary>
        public string? name { get; set; }
        /// <summary>
        /// Gets or sets the managed constant expression, or null before mapping.
        /// </summary>
        public string? value { get; set; }
        /// <summary>
        /// Gets or sets managed item attribute fragments in emission order.
        /// </summary>
        public List<string> attributes { get; set; }
        /// <summary>
        /// Gets or sets the emitted item documentation fragment, or null when absent.
        /// </summary>
        public string? comment { get; set; }

        /// <summary>
        /// Hashes the original native enum-item identifier for in-process metadata lookup.
        /// </summary>
        /// <returns>
        /// The current identifier hash; changing cppName changes this value.
        /// </returns>
        public override int GetHashCode()
        {
            return this.identifier.GetHashCode();
        }

        /// <summary>
        /// Copies the attribute container and all item spellings into an independent projection.
        /// </summary>
        /// <returns>
        /// A separately mutable descriptor retaining the same immutable text values.
        /// </returns>
        public CsEnumItemMetadata Clone()
        {
            return new CsEnumItemMetadata(this.cppName, this.cppValue, this.name, this.value, new List<string>(this.attributes), this.comment);
        }
    }
}
