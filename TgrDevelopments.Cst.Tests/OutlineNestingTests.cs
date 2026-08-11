using System.Collections.Generic;

using Xunit;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Tests;

public class OutlineNestingTests
{
    private static OutlineNode N(string kind, string label, int from, int to, int level = 1, IReadOnlyList<OutlineNode>? ch = null)
    {
        LineMap map = new(string.Join("\n", System.Linq.Enumerable.Repeat("x", to)) + "\n");
        return TextUtil.Outline(kind, label, map, from, to, level, ch);
    }

    [Fact]
    public void NestByLineContainment_MethodInsideClass_BecomesChild()
    {
        OutlineNode cls = N("class", "Engine", 1, 10);
        OutlineNode method = N("method", "run", 3, 5);
        OutlineNode fn = N("function", "main", 12, 14);
        List<OutlineNode> nested = OutlineNesting.NestByLineContainment([cls, method, fn]);
        Assert.Equal(2, nested.Count);
        Assert.Equal("Engine", nested[0].Label);
        Assert.Equal(1, nested[0].Level);
        Assert.Single(nested[0].Children);
        Assert.Equal("run", nested[0].Children[0].Label);
        Assert.Equal(2, nested[0].Children[0].Level);
        Assert.Equal("main", nested[1].Label);
        Assert.Equal(1, nested[1].Level);
    }

    [Fact]
    public void WithChild_SetsLevelParentPlusOne()
    {
        OutlineNode parent = N("class", "A", 1, 5);
        OutlineNode child = N("method", "m", 2, 3, level: 1);
        OutlineNode result = OutlineNesting.WithChild(parent, child);
        Assert.Equal(2, result.Children[0].Level);
    }
}
