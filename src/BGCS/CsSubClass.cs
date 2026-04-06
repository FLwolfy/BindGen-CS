namespace BGCS
{
    using BGCS.Core.CSharp;
    using BGCS.CppAst.Model.Types;

    /// <summary>
    /// Defines the public class <c>CsSubClass</c>.
    /// </summary>
    public class CsSubClass
    {
        /// <summary>
        /// Gets or sets <c>CppType</c>.
        /// </summary>
        public CppType CppType { get; set; }

        /// <summary>
        /// Gets or sets <c>Type</c>.
        /// </summary>
        public CsType Type { get; set; }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets <c>CppFieldName</c>.
        /// </summary>
        public string CppFieldName { get; set; }

        /// <summary>
        /// Gets or sets <c>FieldName</c>.
        /// </summary>
        public string FieldName { get; set; }

        /// <summary>
        /// Initializes a new instance of <see cref="CsSubClass"/>.
        /// </summary>
        public CsSubClass(CppType type, string name, string cppFieldName, string fieldName)
        {
            Type = new(name, name, false, false, false, false, false, false, false, false, false, false, CsStringType.None, CsPrimitiveType.Unknown);
            CppType = type;
            Name = name;
            CppFieldName = cppFieldName;
            FieldName = fieldName;
        }
    }
}
