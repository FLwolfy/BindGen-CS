namespace BGCS.FunctionGeneration.ParameterWriters
{
    using BGCS;
    using BGCS.Core.CSharp;

    /// <summary>
    /// Defines the public class <c>DefaultValueParameterWriter</c>.
    /// </summary>
    public class DefaultValueParameterWriter : IParameterWriter
    {
        /// <summary>
        /// Exposes public member <c>IParameterWriter.PriorityMultiplier</c>.
        /// </summary>
        public virtual int Priority => 6 * IParameterWriter.PriorityMultiplier;

        /// <summary>
        /// Executes public operation <c>CanWrite</c>.
        /// </summary>
        public virtual bool CanWrite(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            return paramFlags.HasFlag(ParameterFlags.Default);
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public virtual void Write(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            var settings = context.Config;
            var paramCsDefault = cppParameter.DefaultValue!;
            if (cppParameter.Type.IsString || paramCsDefault.StartsWith("\"") && paramCsDefault.EndsWith("\""))
            {
                context.AppendParam($"(string){paramCsDefault}");
            }
            else if (cppParameter.Type.IsBool && !cppParameter.Type.IsPointer && !cppParameter.Type.IsArray)
            {
                context.AppendParam($"({settings.GetBoolType()})({paramCsDefault})");
            }
            else if (rootParameter.Type.IsEnum || cppParameter.Type.IsPrimitive || cppParameter.Type.IsPointer || cppParameter.Type.IsArray)
            {
                context.AppendParam($"({rootParameter.Type.Name})({paramCsDefault})");
            }
            else
            {
                context.AppendParam($"{paramCsDefault}");
            }
        }
    }
}
