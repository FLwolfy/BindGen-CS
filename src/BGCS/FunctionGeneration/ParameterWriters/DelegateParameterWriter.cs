namespace BGCS.FunctionGeneration.ParameterWriters
{
    using BGCS.Core.CSharp;

    /// <summary>
    /// Defines the public class <c>DelegateParameterWriter</c>.
    /// </summary>
    public class DelegateParameterWriter : IParameterWriter
    {
        /// <summary>
        /// Gets <c>Priority</c>.
        /// </summary>
        public int Priority { get; } = 3 * IParameterWriter.PriorityMultiplier;

        /// <summary>
        /// Executes public operation <c>CanWrite</c>.
        /// </summary>
        public bool CanWrite(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            return rootParameter.CppType.IsDelegate() && !cppParameter.Type.IsPointer && !cppParameter.Type.IsRef && !cppParameter.Type.IsIn;
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public void Write(FunctionWriterContext context, CsParameterInfo rootParameter, CsParameterInfo cppParameter, ParameterFlags paramFlags, int index, int offset)
        {
            rootParameter.CppType.IsDelegate(out var cppFunction);
            var config = context.Config;
            var ptrType = config.GetDelegatePointerType(cppFunction!);
            context.AppendParam($"({ptrType})Utils.GetFunctionPointerForDelegate({cppParameter.Name})");
        }
    }
}
