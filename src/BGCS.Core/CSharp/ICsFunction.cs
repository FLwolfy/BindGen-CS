namespace BGCS.Core.CSharp
{
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public interface <c>ICsFunction</c>.
    /// </summary>
    public interface ICsFunction
    {
        string ExportedName { get; set; }
        /// <summary>
        /// Gets or sets <c>Kind</c>.
        /// </summary>
        public CsFunctionKind Kind { get; set; }
        string Name { get; set; }
        List<CsParameterInfo> Parameters { get; set; }
        CsType ReturnType { get; set; }
        string StructName { get; set; }

        bool HasParameter(CsParameterInfo cppParameter);

        string ToString();
    }
}
