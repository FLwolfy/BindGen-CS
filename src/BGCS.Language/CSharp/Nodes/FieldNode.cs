namespace BGCS.Language.CSharp.Nodes
{
    using BGCS.Language;

    /// <summary>
    /// Defines the public class <c>FieldNode</c>.
    /// </summary>
    public class FieldNode : SyntaxNode
    {
        /// <summary>
        /// Initializes a new instance of <see cref="FieldNode"/>.
        /// </summary>
        public FieldNode(string type, string name, KeywordType[] modifiers, string? expression)
        {
            Type = type;
            Name = name;
            Modifiers = modifiers;
            Expression = expression;
        }

        /// <summary>
        /// Gets or sets <c>Type</c>.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets <c>Modifiers</c>.
        /// </summary>
        public KeywordType[] Modifiers { get; }

        /// <summary>
        /// Gets or sets <c>Expression</c>.
        /// </summary>
        public string? Expression { get; set; }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return $"field: {string.Join(" ", Modifiers)} {Type} {Name} = {Expression}";
        }
    }
}
