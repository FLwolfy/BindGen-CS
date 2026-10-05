namespace BGCS.Language.Diagnostics
{
    /// <summary>
    /// Records a source position using a UTF-16 offset and human-readable line and column numbers.
    /// </summary>
    public readonly struct SourceLocation
    {
        /// <summary>
        /// Gets the source file name associated with this position.
        /// </summary>
        public readonly string file;
        /// <summary>
        /// Gets the zero-based UTF-16 offset from the beginning of the source.
        /// </summary>
        public readonly int offset;
        /// <summary>
        /// Gets the source line number supplied by the producer; the shared lexer uses zero-based lines.
        /// </summary>
        public readonly int line;
        /// <summary>
        /// Gets the UTF-16 column supplied by the producer; the shared lexer uses zero-based columns.
        /// </summary>
        public readonly int column;
        /// <summary>
        /// Captures a source position without retaining the source text.
        /// </summary>
        /// <param name="file">The source file name.</param>
        /// <param name="offset">The zero-based UTF-16 source offset.</param>
        /// <param name="line">The producer's line number, preserved without normalization.</param>
        /// <param name="column">The producer's UTF-16 column, preserved without normalization.</param>
        public SourceLocation(
            string file,
            int offset,
            int line,
            int column
        ) {
            this.file = file;
            this.offset = offset;
            this.line = line;
            this.column = column;
        }

        /// <summary>
        /// Formats the file, line and column for a human-readable diagnostic.
        /// </summary>
        /// <returns>The source file name followed by its line and column.</returns>
        public override string ToString()
        {
            return $"{this.file} at line: {this.line}, character: {this.column}";
        }
    }
}
