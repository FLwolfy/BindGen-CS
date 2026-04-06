namespace BGCS.FunctionGeneration.ParameterWriters
{
    using BGCS;
    using BGCS.Core.CSharp;

    /// <summary>
    /// Defines the public class <c>ArrayParameterWriter</c>.
    /// </summary>
    public class ArrayParameterWriter : IParameterWriter
    {
        /// <summary>
        /// Exposes public member <c>IParameterWriter.PriorityMultiplier</c>.
        /// </summary>
        public virtual int Priority => 2 * IParameterWriter.PriorityMultiplier;

        /// <summary>
        /// Executes public operation <c>CanWrite</c>.
        /// </summary>
        public virtual bool CanWrite(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            return paramFlags.HasFlag(ParameterFlags.Array);
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public virtual void Write(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            var varName = context.UniqueName($"p{cppParameter.CleanName}");
            context.BeginBlock($"fixed ({cppParameter.Type.CleanName}* {varName} = {cppParameter.Name})");
            context.AppendParam($"({rootParameter.Type.Name}){varName}");
        }
    }
}
