namespace BGCS.Conversion
{
    using System;
    using BGCS.CppAst.Model.Types;

    /// <summary>
    /// Reports a borrowed native type whose spelling cannot be safely converted by the current binding policy.
    /// </summary>
    public class UnexposedTypeException : Exception
    {
        /// <summary>
        /// Captures the diagnostic spelling of an unsupported unexposed native type.
        /// </summary>
        /// <param name="unexposedType">
        /// The borrowed native type used only while constructing the diagnostic message.
        /// </param>
        public UnexposedTypeException(CppUnexposedType unexposedType) : base($"Cannot handle unexposed type '{unexposedType}'")
        {
        }
    }
}
