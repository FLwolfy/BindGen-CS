namespace BGCS.Language.CSharp.Nodes;

    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>NamespaceNode</c>.
    /// </summary>
    public class NamespaceNode : SyntaxNode
    {
        /// <summary>
        /// Initializes a new instance of <see cref="NamespaceNode"/>.
        /// </summary>
        public NamespaceNode(string @namespace)
        {
            Namespace = @namespace;
        }

        /// <summary>
        /// Executes public operation <c>NamespaceNode</c>.
        /// </summary>
        public NamespaceNode(string @namespace, List<SyntaxNode> children) : base(children)
        {
            Namespace = @namespace;
        }

        /// <summary>
        /// Gets <c>Namespace</c>.
        /// </summary>
        public string Namespace { get; }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return $"namespace: {Namespace}";
        }
    }
