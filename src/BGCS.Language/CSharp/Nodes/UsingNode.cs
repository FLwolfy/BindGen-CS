namespace BGCS.Language.CSharp.Nodes
{
    using BGCS.Language;

    public class UsingNode : SyntaxNode
    {
        public UsingNode(string @using)
        {
            Using = @using;
        }

        public string Using { get; }

        public override string ToString()
        {
            return $"using: {Using}";
        }
    }
}