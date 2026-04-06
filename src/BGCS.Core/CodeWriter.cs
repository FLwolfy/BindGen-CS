namespace BGCS
{
    using BGCS.Core;
    using System.Text;

    /// <summary>
    /// Declares the callback signature <c>HeaderInjectionDelegate</c>.
    /// </summary>
    public delegate void HeaderInjectionDelegate(ICodeWriter writer, StringBuilder headerBuilder);

    /// <summary>
    /// Defines the public class <c>CodeWriter</c>.
    /// </summary>
    public sealed class CodeWriter : ICodeWriter, IDisposable
    {
        private readonly string[] _indentStrings;

        private readonly StreamWriter _writer;

        private int lines;
        private int blocks = 0;
        private int indentLevel;

        private string _indentString = "";
        private bool _shouldIndent = true;

        /// <summary>
        /// Exposes public member <c>}</c>.
        /// </summary>
        public int IndentLevel { get => indentLevel; }

        /// <summary>
        /// Exposes public member <c>}</c>.
        /// </summary>
        public string NewLine { get => _writer.NewLine; }

        /// <summary>
        /// Gets <c>FileName</c>.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="CodeWriter"/>.
        /// </summary>
        public CodeWriter(string fileName, string template, HeaderInjectionDelegate? headerInjector)
        {
            fileName = FileNameHelper.SanitizeFileName(fileName);
            FileName = fileName;

            _indentStrings = new string[10];
            for (int i = 0; i < _indentStrings.Length; i++)
            {
                _indentStrings[i] = new string('\t', i);
            }

            _writer = File.CreateText(fileName);
            _writer.NewLine = Environment.NewLine;

            StringBuilder stringBuilder = new(template);

            headerInjector?.Invoke(this, stringBuilder);

            _writer.Write(stringBuilder.ToString());
        }

        /// <summary>
        /// Exposes public member <c>_writer.BaseStream.Length</c>.
        /// </summary>
        public long Length => _writer.BaseStream.Length;

        /// <summary>
        /// Exposes public member <c>lines</c>.
        /// </summary>
        public int Lines => lines;

        /// <summary>
        /// Executes public operation <c>Dispose</c>.
        /// </summary>
        public void Dispose()
        {
            EndBlock();
            _writer.Dispose();
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public void Write(char chr)
        {
            WriteIndented(chr);
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public void Write(string @string)
        {
            WriteIndented(@string);
        }

        /// <summary>
        /// Writes output for <c>WriteLine</c>.
        /// </summary>
        public void WriteLine()
        {
            _writer.WriteLine();
            _shouldIndent = true;
        }

        /// <summary>
        /// Writes output for <c>WriteLine</c>.
        /// </summary>
        public void WriteLine(string @string)
        {
            WriteIndented(@string);
            _writer.WriteLine();
            _shouldIndent = true;
            lines++;
        }

        /// <summary>
        /// Writes output for <c>WriteLines</c>.
        /// </summary>
        public void WriteLines(string? @string)
        {
            if (@string == null)
                return;

            if (@string.Contains('\n'))
            {
                var lines = @string.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    WriteIndented(lines[i]);
                    _shouldIndent = true;
                    this.lines++;
                }
            }
            _shouldIndent = true;
        }

        /// <summary>
        /// Writes output for <c>WriteLines</c>.
        /// </summary>
        public void WriteLines(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                WriteLine(line);
            }
        }

        /// <summary>
        /// Executes public operation <c>BeginBlock</c>.
        /// </summary>
        public void BeginBlock(string content)
        {
            WriteLine(content);
            WriteLine("{");
            Indent(1);
            blocks++;
        }

        /// <summary>
        /// Executes public operation <c>EndBlock</c>.
        /// </summary>
        public void EndBlock()
        {
            if (blocks <= 0)
                return;
            blocks--;
            Unindent(1);
            WriteLine("}");
        }

        /// <summary>
        /// Executes public operation <c>EndBlock</c>.
        /// </summary>
        public void EndBlock(string marker = "}")
        {
            if (blocks <= 0)
                return;
            blocks--;
            Unindent(1);
            WriteLine(marker);
        }

        /// <summary>
        /// Executes public operation <c>PushBlock</c>.
        /// </summary>
        public IDisposable PushBlock(string marker = "{") => new CodeBlock(this, marker);

        /// <summary>
        /// Executes public operation <c>Indent</c>.
        /// </summary>
        public void Indent(int count = 1)
        {
            indentLevel += count;

            if (IndentLevel < _indentStrings.Length)
            {
                _indentString = _indentStrings[IndentLevel];
            }
            else
            {
                _indentString = new string('\t', IndentLevel);
            }
        }

        /// <summary>
        /// Executes public operation <c>Unindent</c>.
        /// </summary>
        public void Unindent(int count = 1)
        {
            if (count > indentLevel)
                throw new ArgumentException("count out of range.", nameof(count));

            indentLevel -= count;
            if (indentLevel < _indentStrings.Length)
            {
                _indentString = _indentStrings[indentLevel];
            }
            else
            {
                _indentString = new string('\t', indentLevel);
            }
        }

        private void WriteIndented(char chr)
        {
            if (_shouldIndent)
            {
                _writer.Write(_indentString);
                _shouldIndent = false;
            }

            _writer.Write(chr);
        }

        private void WriteIndented(string @string)
        {
            if (_shouldIndent)
            {
                _writer.Write(_indentString);
                _shouldIndent = false;
            }

            _writer.Write(@string);
        }

        /// <summary>
        /// Defines the public struct <c>CodeBlock</c>.
        /// </summary>
        public readonly struct CodeBlock : IDisposable
        {
            private readonly CodeWriter _writer;

            /// <summary>
            /// Initializes a new instance of <see cref="CodeBlock"/>.
            /// </summary>
            public CodeBlock(CodeWriter writer, string content)
            {
                _writer = writer;
                _writer.BeginBlock(content);
            }

            /// <summary>
            /// Executes public operation <c>Dispose</c>.
            /// </summary>
            public void Dispose()
            {
                _writer.EndBlock();
            }
        }
    }
}
