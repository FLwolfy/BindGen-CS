namespace BGCS.Language.CSharp.Nodes
{
    /// <summary>
    /// Defines the public class <c>MethodNode</c>.
    /// </summary>
    public class MethodNode : SyntaxNode
    {
        /// <summary>
        /// Initializes a new instance of <see cref="MethodNode"/>.
        /// </summary>
        public MethodNode(string name, KeywordType[] modifiers, string[] parameters, string returnType)
        {
            Name = name;
            Modifiers = modifiers;
            Parameters = parameters;
            ReturnType = returnType;
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
        /// Gets <c>Parameters</c>.
        /// </summary>
        public string[] Parameters { get; }

        /// <summary>
        /// Gets <c>ReturnType</c>.
        /// </summary>
        public string ReturnType { get; }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return $"method: {string.Join(" ", Modifiers)} {ReturnType} {Name} ({string.Join(" ", Parameters)})";
        }
    }
}
