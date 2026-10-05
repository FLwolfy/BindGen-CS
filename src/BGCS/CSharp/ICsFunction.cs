namespace BGCS.CSharp
{
    using System.Collections.Generic;

    /// <summary>
    /// Exposes the mutable signature shared by native overloads and their managed invocation variations during analysis.
    /// </summary>
    public interface ICsFunction
    {
        /// <summary>
        /// Gets or sets the exact native symbol name used for import.
        /// </summary>
        string exportedName { get; set; }
        /// <summary>
        /// Gets or sets whether the projection represents a free function, instance member, or generated invocation form.
        /// </summary>
        public CsFunctionKind kind { get; set; }

        /// <summary>
        /// Gets or sets the managed method name.
        /// </summary>
        string name { get; set; }

        /// <summary>
        /// Gets or sets the mutable parameter model used during overload analysis.
        /// </summary>
        List<CsParameterInfo> parameters { get; set; }

        /// <summary>
        /// Gets or sets the managed return type model.
        /// </summary>
        CsType returnType { get; set; }

        /// <summary>
        /// Gets or sets the containing managed record name for member projections.
        /// </summary>
        string structName { get; set; }

        /// <summary>
        /// Checks whether this function contains the supplied parameter model.
        /// </summary>
        /// <param name="cppParameter">Parameter whose membership is queried.</param>
        /// <returns>True when the parameter belongs to this function; otherwise false.</returns>
        bool HasParameter(CsParameterInfo cppParameter);
        /// <summary>
        /// Renders a diagnostic representation of this function model.
        /// </summary>
        /// <returns>The diagnostic function description; this is not a complete emitted compilation unit.</returns>
        string ToString();
    }
}
