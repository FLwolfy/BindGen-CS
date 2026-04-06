namespace BGCS
{
    using BGCS.Core;
    using BGCS.Core.CSharp;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Defines the public class <c>FunctionWriterContext</c>.
    /// </summary>
    public class FunctionWriterContext
    {
        /// <summary>
        /// Gets <c>Writer</c>.
        /// </summary>
        public ICodeWriter Writer { get; }

        /// <summary>
        /// Gets <c>Config</c>.
        /// </summary>
        public CsCodeGeneratorConfig Config { get; }

        /// <summary>
        /// Gets <c>StringBuilder</c>.
        /// </summary>
        public StringBuilder StringBuilder { get; }

        /// <summary>
        /// Gets <c>Overload</c>.
        /// </summary>
        public CsFunctionOverload Overload { get; }

        /// <summary>
        /// Gets <c>Variation</c>.
        /// </summary>
        public CsFunctionVariation Variation { get; }

        /// <summary>
        /// Gets <c>Flags</c>.
        /// </summary>
        public WriteFunctionFlags Flags { get; }

        private Stack<(string paramName, CsParameterInfo param, string varName, string? convertBackCondition)> strings = new();
        private Stack<(string paramName, CsParameterInfo param, string varName)> stringArrays = new();
        private int stringCounter = 0;
        private int blockCounter = 0;

        /// <summary>
        /// Initializes a new instance of <see cref="FunctionWriterContext"/>.
        /// </summary>
        public FunctionWriterContext(ICodeWriter writer, CsCodeGeneratorConfig config, StringBuilder stringBuilder, CsFunctionOverload overload, CsFunctionVariation variation, WriteFunctionFlags flags)
        {
            Writer = writer;
            Config = config;
            StringBuilder = stringBuilder;
            Overload = overload;
            Variation = variation;
            Flags = flags;
        }

        /// <summary>
        /// Executes public operation <c>AppendParam</c>.
        /// </summary>
        public void AppendParam(string param)
        {
            StringBuilder.Append(param);
        }

        /// <summary>
        /// Executes public operation <c>BeginBlock</c>.
        /// </summary>
        public int BeginBlock(string text)
        {
            Writer.BeginBlock(text);
            return IncrementBlockCounter();
        }

        /// <summary>
        /// Executes public operation <c>EndBlock</c>.
        /// </summary>
        public int EndBlock()
        {
            Writer.EndBlock();
            return DecrementBlockCounter();
        }

        private int IncrementBlockCounter()
        {
            return blockCounter++;
        }

        /// <summary>
        /// Writes output for <c>WriteStringArrayConvertToUnmanaged</c>.
        /// </summary>
        public void WriteStringArrayConvertToUnmanaged(CsParameterInfo parameter)
        {
            MarshalHelper.WriteStringArrayConvertToUnmanaged(Writer, parameter.Type, parameter.Name, $"pStrArray{stringArrays.Count}");
            AppendParam($"pStrArray{stringArrays.Count}");
            PushStringArray(parameter.Name, parameter, $"pStrArray{stringArrays.Count}");
        }

        /// <summary>
        /// Executes public operation <c>PushStringArray</c>.
        /// </summary>
        public void PushStringArray(string paramName, CsParameterInfo parameter, string varName)
        {
            stringArrays.Push((paramName, parameter, varName));
        }

        /// <summary>
        /// Writes output for <c>WriteStringConvertToUnmanaged</c>.
        /// </summary>
        public void WriteStringConvertToUnmanaged(CsParameterInfo parameter, bool isRef, string? convertBackCondition = null)
        {
            // Lightweight branch for readonly strings.
            if (!isRef && parameter.Type.StringType == CsStringType.StringUTF16)
            {
                var varName = UniqueName($"p{parameter.CleanName}");
                BeginBlock($"fixed (char* {varName} = {parameter.Name})");
                AppendParam(varName);
                return;
            }

            int stringCounter = IncrementStringCounter();
            if (isRef)
            {
                PushString(parameter.Name, parameter, $"pStr{stringCounter}", convertBackCondition);
            }

            MarshalHelper.WriteStringConvertToUnmanaged(Writer, parameter.Type, parameter.Name, stringCounter);
            AppendParam($"pStr{stringCounter}");
        }

        /// <summary>
        /// Executes public operation <c>PushString</c>.
        /// </summary>
        public void PushString(string paramName, CsParameterInfo parameter, string varName, string? convertBackCondition)
        {
            strings.Push((paramName, parameter, varName, convertBackCondition));
        }

        /// <summary>
        /// Executes public operation <c>IncrementStringCounter</c>.
        /// </summary>
        public int IncrementStringCounter()
        {
            return stringCounter++;
        }

        /// <summary>
        /// Executes public operation <c>TryPopString</c>.
        /// </summary>
        public bool TryPopString(out (string paramName, CsParameterInfo param, string varName, string? convertBackCondition) stackItem)
        {
            return strings.TryPop(out stackItem);
        }

        /// <summary>
        /// Executes public operation <c>TryPopStringArray</c>.
        /// </summary>
        public bool TryPopStringArray(out (string paramName, CsParameterInfo param, string varName) stackItem)
        {
            return stringArrays.TryPop(out stackItem);
        }

        /// <summary>
        /// Exposes public member <c>stringCounter</c>.
        /// </summary>
        public int StringCounter => stringCounter;

        /// <summary>
        /// Exposes public member <c>blockCounter</c>.
        /// </summary>
        public int BlockCounter => blockCounter;

        /// <summary>
        /// Executes public operation <c>DecrementBlockCounter</c>.
        /// </summary>
        public int DecrementBlockCounter()
        {
            return blockCounter--;
        }

        /// <summary>
        /// Executes public operation <c>DecrementStringCounter</c>.
        /// </summary>
        public int DecrementStringCounter()
        {
            return stringCounter--;
        }

        /// <summary>
        /// Executes public operation <c>ConvertStrings</c>.
        /// </summary>
        public void ConvertStrings()
        {
            while (strings.TryPop(out var stackItem))
            {
                MarshalHelper.WriteStringConvertToManaged(Writer, stackItem.param.Type, stackItem.paramName, stackItem.varName, stackItem.convertBackCondition);
            }
        }

        /// <summary>
        /// Executes public operation <c>FreeStringArrays</c>.
        /// </summary>
        public void FreeStringArrays()
        {
            while (stringArrays.TryPop(out var stackItem))
            {
                MarshalHelper.WriteFreeUnmanagedStringArray(Writer, stackItem.paramName, stackItem.varName);
            }
        }

        /// <summary>
        /// Executes public operation <c>FreeStrings</c>.
        /// </summary>
        public void FreeStrings()
        {
            while (stringCounter > 0)
            {
                stringCounter--;
                MarshalHelper.WriteFreeString(Writer, stringCounter);
            }
        }

        /// <summary>
        /// Executes public operation <c>EndBlocks</c>.
        /// </summary>
        public void EndBlocks()
        {
            while (blockCounter > 0)
            {
                blockCounter--;
                Writer.EndBlock();
            }
        }

        /// <summary>
        /// Executes public operation <c>UniqueName</c>.
        /// </summary>
        public string UniqueName(string name)
        {
            var nname = name;
            int i = 0;
            while (true)
            {
                bool found = false;
                foreach (var param in Variation.Parameters)
                {
                    if (param.DefaultValue == null && param.CleanName == nname)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    break;
                }

                nname = $"{name}{i}";
                i++;
            }

            return nname;
        }
    }
}
