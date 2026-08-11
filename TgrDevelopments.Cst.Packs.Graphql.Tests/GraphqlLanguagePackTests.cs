using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Graphql;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Graphql.Tests;

public class GraphqlLanguagePackTests : StructuralPackTestBase
{
    private readonly GraphqlLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "type A { x: Int }\n" + new string(' ', 100);
    protected override string FuzzSource => """
type User { id: ID! }
type Query { user: User }
""";
    protected override string BrokenSource => "type X {\n";

    [Fact]
    public void Parse_Happy_TypeLabels()
    {
        string src = """
type User { id: ID! }
type Query { user: User }
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Equal(2, outline.Count);
        Assert.Equal("type", outline[0].Kind);
        Assert.Equal("User", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal("type", outline[1].Kind);
        Assert.Equal("Query", outline[1].Label);
    }

    [Fact]
    public void Parse_StringSafety_NoFalseType()
    {
        string src = "type Real { description: String }\nquery Op { real }\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == "Real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseType()
    {
        string src = "# type Fake { x: Int }\ntype Real { x: Int }\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "Fake");
        Assert.Contains(outline, static o => o.Label == "Real");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("type Alpha { x: Int }\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("type Beta { x: Int }\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }
}
