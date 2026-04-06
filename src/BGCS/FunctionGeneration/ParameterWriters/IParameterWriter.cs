namespace BGCS.FunctionGeneration.ParameterWriters
{
    using BGCS;
    using BGCS.Core.CSharp;

    /// <summary>
    /// Defines the public interface <c>IParameterWriter</c>.
    /// </summary>
    public interface IParameterWriter
    {
        int Priority { get; }

        bool CanWrite(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset);

        void Write(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset);

        /// <summary>
        /// Exposes public member <c>10000</c>.
        /// </summary>
        public const int PriorityMultiplier = 10000;
    }
}
