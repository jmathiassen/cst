using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Css;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Css.Tests;

public class CssLanguagePackTests : StructuralPackTestBase
{
    private readonly CssLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => ".a { color: red; }\n" + new string(' ', 100);
    protected override string FuzzSource => """
.header { color: red; }
@media (min-width: 600px) {
  .header { color: blue; }
}
""";
    protected override string BrokenSource => ".a { color: red;\n";

    [Fact]
    public void Parse_Happy_RuleAndMediaLabels()
    {
        string src = """
.header { color: red; }
@media (min-width: 600px) {
  .header { color: blue; }
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);

        Assert.Equal("rule", outline[0].Kind);
        Assert.Equal(".header", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);

        Assert.Equal("at-rule", outline[1].Kind);
        Assert.Contains("@media", outline[1].Label);
        Assert.Equal(1, outline[1].Level);
        Assert.Equal(2, outline[1].LineSpan.Start.Line);
        Assert.NotNull(outline[1].Children);
        Assert.Contains(outline[1].Children, static o => o.Kind == "rule" && o.Label == ".header" && o.Level == 2);
    }

    [Fact]
    public void Parse_StringSafety_NoFalseRule()
    {
        string src = ".real { color: red; }\n.real2 { content: \".fake { }\"; }\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == ".real");
        Assert.DoesNotContain(outline, static o => o.Label == ".fake");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseRule()
    {
        string src = "/* .fake { color: red; } */\n.real { color: blue; }\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == ".real");
        Assert.DoesNotContain(outline, static o => o.Label == ".fake");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From(".a { color: red; }\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From(".b { color: blue; }\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == ".a");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == ".b");
    }
}
