namespace BGCS.FunctionGeneration.ParameterWriters
{
    using BGCS;
    using BGCS.Core.CSharp;

    /// <summary>
    /// Defines the public class <c>StringParameterWriter</c>.
    /// </summary>
    public class StringParameterWriter : IParameterWriter
    {
        /// <summary>
        /// Exposes public member <c>IParameterWriter.PriorityMultiplier</c>.
        /// </summary>
        public virtual int Priority => 5 * IParameterWriter.PriorityMultiplier;

        /// <summary>
        /// Executes public operation <c>CanWrite</c>.
        /// </summary>
        public virtual bool CanWrite(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            return paramFlags.HasFlag(ParameterFlags.String);
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public virtual void Write(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            if (paramFlags.HasFlag(ParameterFlags.Array))
            {
                context.WriteStringArrayConvertToUnmanaged(cppParameter);
            }
            else
            {
                context.WriteStringConvertToUnmanaged(cppParameter, paramFlags.HasFlag(ParameterFlags.Ref), GetConvertBackCondition(context, rootParameter, cppParameter, paramFlags));
            }
        }

        protected virtual string? GetConvertBackCondition(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags)
        {
            return null;
        }
    }
}
