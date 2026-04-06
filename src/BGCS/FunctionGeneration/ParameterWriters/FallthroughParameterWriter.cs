namespace BGCS.FunctionGeneration.ParameterWriters
{
    using BGCS;
    using BGCS.Core.CSharp;

    /// <summary>
    /// Defines the public class <c>FallthroughParameterWriter</c>.
    /// </summary>
    public class FallthroughParameterWriter : IParameterWriter
    {
        /// <summary>
        /// Exposes public member <c>int.MinValue</c>.
        /// </summary>
        public virtual int Priority => int.MinValue;

        /// <summary>
        /// Executes public operation <c>CanWrite</c>.
        /// </summary>
        public virtual bool CanWrite(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            return true;
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public virtual void Write(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            if (rootParameter.Type.Name != cppParameter.Type.Name)
            {
                context.AppendParam($"({rootParameter.Type.Name}){cppParameter.Name}");
            }
            else
            {
                context.AppendParam(cppParameter.Name);
            }
        }
    }
}
