using System;
using System.Collections.Generic;
using System.Text;
using BGCS.Language.Syntax;
using Xunit;

namespace BGCS.Language.Tests;

public sealed class SyntaxNodeOwnershipTests
{
    [Fact]
    public void InitialChildrenAreCopiedAndPublicViewRejectsMutation()
    {
        var child = new RootNode();
        var source = new List<SyntaxNode> { child };
        var root = new RootNode(source);
        source.Clear();

        Assert.True(root.Contains(child));
        Assert.Single(root.children);
        Assert.Throws<NotSupportedException>(() => ((IList<SyntaxNode>)root.children).Clear());
        root.RemoveChild(child);
        Assert.False(root.Contains(child));
    }

    [Fact]
    public void DebugRenderingSupportsDeepTreesAndRestoresCallerIndentation()
    {
        var root = new RootNode();
        SyntaxNode current = root;
        for (int index = 0; index < 32; index++)
        {
            var child = new RootNode();
            current.AddChild(child);
            current = child;
        }
        var output = new StringBuilder();
        int level = 2;
        root.BuildDebugTree(output, ref level);

        Assert.Equal(2, level);
        Assert.StartsWith("\t\troot", output.ToString());
        Assert.Contains(new string('\t', 34) + "root", output.ToString());
    }
}
