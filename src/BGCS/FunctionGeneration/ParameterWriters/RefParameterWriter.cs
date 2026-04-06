namespace BGCS.FunctionGeneration.ParameterWriters
{
    using BGCS;
    using BGCS.Core.CSharp;

    /// <summary>
    /// Defines the public class <c>RefParameterWriter</c>.
    /// </summary>
    public class RefParameterWriter : IParameterWriter
    {
        /// <summary>
        /// Exposes public member <c>IParameterWriter.PriorityMultiplier</c>.
        /// </summary>
        public virtual int Priority => 4 * IParameterWriter.PriorityMultiplier;

        /// <summary>
        /// Executes public operation <c>CanWrite</c>.
        /// </summary>
        public virtual bool CanWrite(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            return (paramFlags & (ParameterFlags.Ref | ParameterFlags.Out | ParameterFlags.In)) != 0;
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public virtual void Write(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            var varName = context.UniqueName($"p{cppParameter.CleanName}");
            context.BeginBlock($"fixed ({cppParameter.Type.CleanName}* {varName} = &{cppParameter.Name})");
            context.AppendParam($"({rootParameter.Type.Name}){varName}");
        }
    }
}
