namespace BGCS.Configuration.Mapping
{
    using BGCS.CppAst.Model.Types;

    /// <summary>
    /// Selects a managed inline-array carrier by its native primitive kind and fixed element count.
    /// </summary>
    public class ArrayMapping
    {
        private CppPrimitiveKind m_primitive;
        private int m_size;
        private string m_name;
        /// <summary>
        /// Captures the primitive/count selector and managed carrier identifier.
        /// </summary>
        /// <param name="primitive">
        /// The native primitive element kind to match.
        /// </param>
        /// <param name="size">
        /// The fixed element count to match, rather than the byte size.
        /// </param>
        /// <param name="name">
        /// The managed carrier identifier used for matching arrays.
        /// </param>
        public ArrayMapping(
            CppPrimitiveKind primitive,
            int size,
            string name
        ) {
            this.m_primitive = primitive;
            this.m_size = size;
            this.m_name = name;
        }

        /// <summary>
        /// Gets or sets the native primitive element kind selected by this rule.
        /// </summary>
        public CppPrimitiveKind primitive { get => this.m_primitive; set => this.m_primitive = value; }
        /// <summary>
        /// Gets or sets the fixed element count selected by this rule.
        /// </summary>
        public int size { get => this.m_size; set => this.m_size = value; }
        /// <summary>
        /// Gets or sets the managed carrier identifier used for matching arrays.
        /// </summary>
        public string name { get => this.m_name; set => this.m_name = value; }
    }
}
