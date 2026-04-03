namespace BGCS
{
    using BGCS.CppAst.Model.Types;
    using System;

    public class UnexposedTypeException : Exception
    {
        public UnexposedTypeException(CppUnexposedType unexposedType) : base($"Cannot handle unexposed type '{unexposedType}'")
        {
        }
    }
}