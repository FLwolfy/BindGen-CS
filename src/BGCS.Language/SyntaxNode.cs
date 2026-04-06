namespace BGCS.Language
{
    using System.Text;

    /// <summary>
    /// Defines the public class <c>SyntaxNode</c>.
    /// </summary>
    public abstract class SyntaxNode
    {
        protected List<SyntaxNode> children = new();

        /// <summary>
        /// Initializes a new instance of <see cref="SyntaxNode"/>.
        /// </summary>
        public SyntaxNode()
        {
        }

        /// <summary>
        /// Executes public operation <c>SyntaxNode</c>.
        /// </summary>
        public SyntaxNode(List<SyntaxNode> children)
        {
            this.children = children;
        }

        /// <summary>
        /// Exposes public member <c>children</c>.
        /// </summary>
        public IReadOnlyList<SyntaxNode> Children => children;

        /// <summary>
        /// Adds data or behavior through <c>AddChild</c>.
        /// </summary>
        public void AddChild(SyntaxNode node)
        {
            children.Add(node);
        }

        /// <summary>
        /// Returns computed data from <c>GetChild</c>.
        /// </summary>
        public SyntaxNode GetChild(int index)
        {
            return children[index];
        }

        /// <summary>
        /// Removes data or behavior through <c>RemoveChild</c>.
        /// </summary>
        public void RemoveChild(SyntaxNode node)
        {
            children.Remove(node);
        }

        /// <summary>
        /// Removes data or behavior through <c>RemoveChildAt</c>.
        /// </summary>
        public void RemoveChildAt(int index)
        {
            children.RemoveAt(index);
        }

        /// <summary>
        /// Executes public operation <c>Contains</c>.
        /// </summary>
        public void Contains(SyntaxNode node)
        {
            children.Contains(node);
        }

        private const string intendString = "\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t";

        /// <summary>
        /// Executes public operation <c>BuildDebugTree</c>.
        /// </summary>
        public void BuildDebugTree(StringBuilder sb, ref int level)
        {
            sb.Append(intendString.AsSpan(0, level));
            sb.AppendLine(ToString());
            level++;
            for (int i = 0; i < children.Count; i++)
            {
                children[i].BuildDebugTree(sb, ref level);
            }
            level--;
        }
    }
}
