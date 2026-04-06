namespace BGCS.Language.CSharp.Nodes
{
    /// <summary>
    /// Defines the public class <c>ClassNode</c>.
    /// </summary>
    public class ClassNode : SyntaxNode
    {
        /// <summary>
        /// Initializes a new instance of <see cref="ClassNode"/>.
        /// </summary>
        public ClassNode(string name, KeywordType[] modifiers)
        {
            Name = name;
            Modifiers = modifiers;
        }

        /// <summary>
        /// Executes public operation <c>ClassNode</c>.
        /// </summary>
        public ClassNode(string name, KeywordType[] modifiers, List<SyntaxNode> children) : base(children)
        {
            Name = name;
            Modifiers = modifiers;
        }

        /// <summary>
        /// Gets <c>Name</c>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets <c>Modifiers</c>.
        /// </summary>
        public KeywordType[] Modifiers { get; }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return $"class: {string.Join(" ", Modifiers)} {Name}";
        }
    }
}
