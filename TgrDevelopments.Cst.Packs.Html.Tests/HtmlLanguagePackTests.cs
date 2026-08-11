using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Html;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Html.Tests;

public class HtmlLanguagePackTests : StructuralPackTestBase
{
    private readonly HtmlLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "<html><head><title>Hi</title></head><body><h1>Title</h1><section><h2>S</h2></section></body></html>\n";
    protected override string FuzzSource => """
<html><head><title>Hi</title></head><body><h1>Title</h1><section><h2>S</h2></section></body></html>
""";
    protected override string BrokenSource => "<div><span>\n";

    [Fact]
    public void Parse_Happy_NestedElements()
    {
        string src = """
<html><head><title>Hi</title></head><body><h1>Title</h1><section><h2>S</h2></section></body></html>
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Kind == "element" && o.Label == "html");
        Assert.Contains(outline, static o => o.Kind == "element" && o.Label == "body");
        OutlineNode body = Assert.Single(outline, static o => o.Label == "body");
        Assert.Contains(body.Children, static c => c.Label == "h1");
        Assert.Contains(body.Children, static c => c.Label == "section");
        OutlineNode section = Assert.Single(body.Children, static c => c.Label == "section");
        Assert.Contains(section.Children, static c => c.Label == "h2" && c.Level == 3);
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("<main id=\"a\"></main>\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("<nav id=\"b\"></nav>\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "main");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "nav");
        Assert.DoesNotContain(_pack.GetOutline(a!), static o => o.Label == "nav");
    }

    [Fact]
    public void Parse_StringSafety_NoFalseElement()
    {
        string src = "<div title=\"main\">text</div>\n<section></section>\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "main");
        Assert.Contains(outline, static o => o.Label == "section");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseElement()
    {
        string src = "<!-- <section></section> -->\n<main></main>\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "section");
        Assert.Contains(outline, static o => o.Label == "main");
    }
}
