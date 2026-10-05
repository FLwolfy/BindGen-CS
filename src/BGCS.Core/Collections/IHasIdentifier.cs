namespace BGCS.Core.Collections
{
    /// <summary>
    /// Supplies the semantic identifier used to compare metadata entries independently of object identity.
    /// </summary>
    public interface IHasIdentifier
    {
        /// <summary>
        /// Gets the non-null identifier compared with ordinal, case-sensitive string equality.
        /// </summary>
        public string identifier { get; }
    }
}
