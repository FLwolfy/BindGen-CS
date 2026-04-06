namespace BGCS.Language.CSharp.Nodes
{
    using BGCS.Language;

    /// <summary>
    /// Defines the public class <c>UsingNode</c>.
    /// </summary>
    public class UsingNode : SyntaxNode
    {
        /// <summary>
        /// Initializes a new instance of <see cref="UsingNode"/>.
        /// </summary>
        public UsingNode(string @using)
        {
            Using = @using;
        }

        /// <summary>
        /// Gets <c>Using</c>.
        /// </summary>
        public string Using { get; }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return $"using: {Using}";
        }
    }
}
