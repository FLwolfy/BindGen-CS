namespace BGCS.Core.Mapping
{
    using CppAst;
    using BGCS.CppAst.Model.Types;

    /// <summary>
    /// Defines the public class <c>ArrayMapping</c>.
    /// </summary>
    public class ArrayMapping
    {
        private CppPrimitiveKind primitive;
        private int size;
        private string name;

        /// <summary>
        /// Initializes a new instance of <see cref="ArrayMapping"/>.
        /// </summary>
        public ArrayMapping(CppPrimitiveKind primitive, int size, string name)
        {
            this.primitive = primitive;
            this.size = size;
            this.name = name;
        }

        /// <summary>
        /// Exposes public member <c>}</c>.
        /// </summary>
        public CppPrimitiveKind Primitive { get => primitive; set => primitive = value; }

        /// <summary>
        /// Exposes public member <c>}</c>.
        /// </summary>
        public int Size { get => size; set => size = value; }

        /// <summary>
        /// Exposes public member <c>}</c>.
        /// </summary>
        public string Name { get => name; set => name = value; }
    }
}
