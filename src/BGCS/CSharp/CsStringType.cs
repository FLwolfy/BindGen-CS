namespace BGCS.CSharp
{
    /// <summary>
    /// Selects the encoding convention of a managed/native string projection.
    /// </summary>
    public enum CsStringType
    {
        /// <summary>
        /// The value is not a native string projection.
        /// </summary>
        None,
        /// <summary>
        /// A string encoded as UTF-8 bytes.
        /// </summary>
        StringUTF8,
        /// <summary>
        /// A string encoded as UTF-16 code units.
        /// </summary>
        StringUTF16,
        /// <summary>
        /// A length-prefixed COM BSTR string.
        /// </summary>
        StringBSTR,
    }
}
