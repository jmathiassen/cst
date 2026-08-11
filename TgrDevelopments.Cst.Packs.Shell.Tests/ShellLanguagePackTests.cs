using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Shell;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Shell.Tests;

public class ShellLanguagePackTests : StructuralPackTestBase
{
    private readonly ShellLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "greet() {\n  true\n}\n" + new string(' ', 100);
    protected override string FuzzSource => """
#!/usr/bin/env bash
greet() {
  echo hi
}
main() {
  greet
}
""";
    protected override string BrokenSource => "greet() {\n";

    [Fact]
    public void Parse_Happy_FunctionLabelsAndLines()
    {
        string src = """
#!/usr/bin/env bash
greet() {
  echo hi
}
main() {
  greet
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Same(tree.CachedOutline, outline);
        Assert.Equal(2, outline.Count);
        Assert.Equal("function", outline[0].Kind);
        Assert.Equal("greet", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal(2, outline[0].LineSpan.Start.Line);
        Assert.Equal(4, outline[0].LineSpan.End.Line);
        Assert.Equal("function", outline[1].Kind);
        Assert.Equal("main", outline[1].Label);
        Assert.Equal(5, outline[1].LineSpan.Start.Line);
        Assert.Equal(7, outline[1].LineSpan.End.Line);
    }

    [Fact]
    public void Parse_StringSafety_NoFalseFunction()
    {
        string src = "x=\"greet() {\"\ngreet() {\n  true\n}\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == "greet");
        Assert.Equal(1, outline.Count(static o => o.Label == "greet"));
    }

    [Fact]
    public void Parse_CommentSafety_KeywordsInCommentsNoStructure()
    {
        string src = "# greet() { echo hi; }\nmain() {\n  true\n}\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == "main");
        Assert.DoesNotContain(outline, static o => o.Label == "greet");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("alpha() {\n  true\n}\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("beta() {\n  true\n}\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "beta");
    }
}
