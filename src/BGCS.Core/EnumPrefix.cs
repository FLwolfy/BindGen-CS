namespace BGCS
{
    /// <summary>
    /// Defines the public struct <c>EnumPrefix</c>.
    /// </summary>
    public struct EnumPrefix
    {
        /// <summary>
        /// Exposes public member <c>Parts</c>.
        /// </summary>
        public string[] Parts;

        /// <summary>
        /// Initializes a new instance of <see cref="EnumPrefix"/>.
        /// </summary>
        public EnumPrefix(string[] prefixes)
        {
            Parts = prefixes;
        }
    }
}
