namespace BGCS.Language
{
    using System.Text;

    /// <summary>
    /// Defines the public class <c>SyntaxTree</c>.
    /// </summary>
    public class SyntaxTree
    {
        private readonly RootNode root;

        /// <summary>
        /// Initializes a new instance of <see cref="SyntaxTree"/>.
        /// </summary>
        public SyntaxTree(RootNode root)
        {
            this.root = root;
        }

        /// <summary>
        /// Exposes public member <c>root.Children</c>.
        /// </summary>
        public IReadOnlyList<SyntaxNode> Nodes => root.Children;

        /// <summary>
        /// Executes public operation <c>BuildDebugTree</c>.
        /// </summary>
        public string BuildDebugTree()
        {
            StringBuilder sb = new();
            sb.AppendLine(root.ToString());
            var level = 1;

            for (int i = 0; i < root.Children.Count; i++)
            {
                root.Children[i].BuildDebugTree(sb, ref level);
            }

            return sb.ToString();
        }
    }
}
