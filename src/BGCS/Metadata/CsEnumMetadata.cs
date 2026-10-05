using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Collections;

namespace BGCS.Metadata
{
    using Newtonsoft.Json;

    /// <summary>
    /// Retains a native enum identity and its mutable managed carrier, item, attribute, and documentation projections.
    /// </summary>
    public class CsEnumMetadata : IHasIdentifier, ICloneable<CsEnumMetadata>
    {
        /// <summary>
        /// Retains the enum projection, using int when no explicit carrier is supplied and creating absent item collections as empty.
        /// </summary>
        /// <param name="cppName">
        /// The original native enum identifier.
        /// </param>
        /// <param name="name">
        /// The mapped managed enum identifier.
        /// </param>
        /// <param name="attributes">
        /// Managed attribute fragments; supplied lists are retained, while null creates an empty list.
        /// </param>
        /// <param name="comment">
        /// The emitted documentation fragment, or null when absent.
        /// </param>
        /// <param name="baseType">
        /// The managed integral carrier spelling; blank input selects int.
        /// </param>
        /// <param name="items">
        /// Mapped enum items; supplied lists are retained, while null creates an empty list.
        /// </param>
        [JsonConstructor]
        public CsEnumMetadata(
            string cppName,
            string name,
            List<string>? attributes,
            string? comment,
            string baseType,
            List<CsEnumItemMetadata>? items
        ) {
            this.cppName = cppName;
            this.name = name;
            this.attributes = attributes ?? [];
            this.comment = comment;
            this.items = items ?? [];
            this.baseType = string.IsNullOrWhiteSpace(baseType) ? "int" : baseType;
        }

        /// <summary>
        /// Retains the enum projection, using int when no explicit carrier is supplied and creating absent item collections as empty.
        /// </summary>
        /// <param name="cppName">
        /// The original native enum identifier.
        /// </param>
        /// <param name="name">
        /// The mapped managed enum identifier.
        /// </param>
        /// <param name="attributes">
        /// Managed attribute fragments; supplied lists are retained, while null creates an empty list.
        /// </param>
        /// <param name="comment">
        /// The emitted documentation fragment, or null when absent.
        /// </param>
        /// <param name="items">
        /// Mapped enum items; supplied lists are retained, while null creates an empty list.
        /// </param>
        public CsEnumMetadata(
            string cppName,
            string name,
            List<string> attributes,
            string? comment,
            List<CsEnumItemMetadata> items
        ) {
            this.cppName = cppName;
            this.name = name;
            this.attributes = attributes;
            this.comment = comment;
            this.items = items;
            this.baseType = "int";
        }

        /// <summary>
        /// Retains the enum projection, using int when no explicit carrier is supplied and creating absent item collections as empty.
        /// </summary>
        /// <param name="cppName">
        /// The original native enum identifier.
        /// </param>
        /// <param name="name">
        /// The mapped managed enum identifier.
        /// </param>
        /// <param name="attributes">
        /// Managed attribute fragments; supplied lists are retained, while null creates an empty list.
        /// </param>
        /// <param name="comment">
        /// The emitted documentation fragment, or null when absent.
        /// </param>
        public CsEnumMetadata(
            string cppName,
            string name,
            List<string> attributes,
            string? comment
        ) {
            this.cppName = cppName;
            this.name = name;
            this.attributes = attributes;
            this.comment = comment;
            this.items = new();
            this.baseType = "int";
        }

        /// <summary>
        /// Gets the original native enum identifier used for metadata matching.
        /// </summary>
        public string identifier => this.cppName;
        /// <summary>
        /// Gets or sets the original native enum identifier.
        /// </summary>
        public string cppName { get; set; }
        /// <summary>
        /// Gets or sets the mapped managed enum identifier.
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Gets or sets managed enum attribute fragments in emission order.
        /// </summary>
        public List<string> attributes { get; set; }
        /// <summary>
        /// Gets or sets the emitted enum documentation fragment, or null when absent.
        /// </summary>
        public string? comment { get; set; }
        /// <summary>
        /// Gets or sets the managed integral carrier spelling.
        /// </summary>
        public string baseType { get; set; }
        /// <summary>
        /// Gets or sets the mutable mapped enum items in declaration order.
        /// </summary>
        public List<CsEnumItemMetadata> items { get; set; } = new();

        /// <summary>
        /// Hashes the original native enum identifier for in-process metadata lookup.
        /// </summary>
        /// <returns>
        /// The current identifier hash; changing cppName changes this value.
        /// </returns>
        public override int GetHashCode()
        {
            return this.identifier.GetHashCode();
        }

        /// <summary>
        /// Copies attribute and item collections, cloning each mapped item and preserving the managed carrier spelling.
        /// </summary>
        /// <returns>
        /// A separately mutable enum projection retaining the same immutable text values.
        /// </returns>
        public CsEnumMetadata Clone()
        {
            return new CsEnumMetadata(this.cppName, this.name, new List<string>(this.attributes), this.comment, this.baseType, this.items.Select(item => item.Clone()).ToList());
        }
    }
}
