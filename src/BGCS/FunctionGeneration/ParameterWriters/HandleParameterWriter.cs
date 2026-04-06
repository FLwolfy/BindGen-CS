namespace BGCS.FunctionGeneration.ParameterWriters
{
    using BGCS;
    using BGCS.Core.CSharp;

    /// <summary>
    /// Defines the public class <c>HandleParameterWriter</c>.
    /// </summary>
    public class HandleParameterWriter : IParameterWriter
    {
        /// <summary>
        /// Exposes public member <c>IParameterWriter.PriorityMultiplier</c>.
        /// </summary>
        public virtual int Priority => 8 * IParameterWriter.PriorityMultiplier;

        /// <summary>
        /// Executes public operation <c>CanWrite</c>.
        /// </summary>
        public virtual bool CanWrite(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            var writeFunctionFlags = context.Flags;
            return writeFunctionFlags.HasFlag(WriteFunctionFlags.UseHandle) && index == 0;
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public virtual void Write(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            context.AppendParam("Handle");
        }
    }
}
