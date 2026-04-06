namespace BGCS
{
    using BGCS.Core;

    /// <summary>
    /// Defines the public class <c>DummyCodeWriter</c>.
    /// </summary>
    public sealed class DummyCodeWriter : ICodeWriter, IDisposable
    {
        private int indentLevel;

        /// <summary>
        /// Exposes public member <c>}</c>.
        /// </summary>
        public int IndentLevel { get => indentLevel; }

        /// <summary>
        /// Executes public operation <c>BeginBlock</c>.
        /// </summary>
        public void BeginBlock(string content)
        {
        }

        /// <summary>
        /// Executes public operation <c>Dispose</c>.
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>
        /// Executes public operation <c>EndBlock</c>.
        /// </summary>
        public void EndBlock(string marker = "}")
        {
        }

        /// <summary>
        /// Executes public operation <c>Indent</c>.
        /// </summary>
        public void Indent(int count = 1)
        {
            indentLevel += count;
        }

        /// <summary>
        /// Executes public operation <c>PushBlock</c>.
        /// </summary>
        public IDisposable PushBlock(string marker = "{")
        {
            return new DummyBlock(this);
        }

        private struct DummyBlock : IDisposable
        {
            private DummyCodeWriter dummyCodeWriter;

            /// <summary>
            /// Initializes a new instance of <see cref="DummyBlock"/>.
            /// </summary>
            public DummyBlock(DummyCodeWriter dummyCodeWriter)
            {
                this.dummyCodeWriter = dummyCodeWriter;
            }

            /// <summary>
            /// Executes public operation <c>Dispose</c>.
            /// </summary>
            public void Dispose()
            {
                dummyCodeWriter.EndBlock();
            }
        }

        /// <summary>
        /// Executes public operation <c>Unindent</c>.
        /// </summary>
        public void Unindent(int count = 1)
        {
            indentLevel -= count;
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public void Write(char chr)
        {
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public void Write(string @string)
        {
        }

        /// <summary>
        /// Writes output for <c>WriteLine</c>.
        /// </summary>
        public void WriteLine()
        {
        }

        /// <summary>
        /// Writes output for <c>WriteLine</c>.
        /// </summary>
        public void WriteLine(string @string)
        {
        }

        /// <summary>
        /// Writes output for <c>WriteLines</c>.
        /// </summary>
        public void WriteLines(string? @string)
        {
        }

        /// <summary>
        /// Writes output for <c>WriteLines</c>.
        /// </summary>
        public void WriteLines(IEnumerable<string> lines)
        {
        }
    }
}
