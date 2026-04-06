namespace BGCS
{
    using BGCS.CppAst.Model.Types;
    using System;

    /// <summary>
    /// Defines the public class <c>UnexposedTypeException</c>.
    /// </summary>
    public class UnexposedTypeException : Exception
    {
        /// <summary>
        /// Initializes a new instance of <see cref="UnexposedTypeException"/>.
        /// </summary>
        public UnexposedTypeException(CppUnexposedType unexposedType) : base($"Cannot handle unexposed type '{unexposedType}'")
        {
        }
    }
}
