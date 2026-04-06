namespace BGCS.Language.CSharp.Nodes
{
    /// <summary>
    /// Defines the public class <c>ExpressionNode</c>.
    /// </summary>
    public class ExpressionNode : SyntaxNode
    {
        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            if (Children.Count == 0) return "expression: <Empty>";
            return "";
        }
    }
}
