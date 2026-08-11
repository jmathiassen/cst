using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Markdown;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Markdown.Tests;

public class MarkdownPackTests : StructuralPackTestBase
{
    private readonly MarkdownLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "# Title\n\n" + new string('x', 100);
    protected override string FuzzSource => "# Title\n\nIntro text.\n\n## Section\n\n```csharp\nvar x = 1;\n```\n\n### Nested\n";
    protected override string BrokenSource => "# H\n\n```\ncode only\n";

    [Fact]
    public void Parse_Happy_HeadingsAndFence_Outline()
    {
        SourceText source = SourceText.From("# Title\n\nIntro text.\n\n## Section\n\n```csharp\nvar x = 1;\n```\n\n### Nested\n");
        ConcreteSyntaxTree tree = _pack.Parse(source);
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.True(outline.Count >= 1);
        Assert.Equal("heading", outline[0].Kind);
        Assert.Equal("Title", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.True(outline[0].LineSpan.Start.Line >= 1);
        Assert.True(outline[0].LineSpan.End.Line >= outline[0].LineSpan.Start.Line);
        OutlineNode? section = outline[0].Children.FirstOrDefault(c => c.Label == "Section");
        Assert.NotNull(section);
        Assert.Equal(2, section!.Level);
        Assert.Contains(section.Children, c => c.Kind == "code_fence" && c.Label == "csharp");
        Assert.Contains(section.Children, c => c.Kind == "heading" && c.Label == "Nested");
    }

    [Fact]
    public void Parse_Broken_UnclosedFence_Diagnostic()
    {
        SourceText source = SourceText.From("# H\n\n```\ncode only\n");
        ConcreteSyntaxTree tree = _pack.Parse(source);
        Assert.Contains(tree.Diagnostics, d => d.Code == "MD001");
        Assert.True(tree.IsPartial);
    }

    [Fact]
    public void Parse_SetextHeading_Recognized()
    {
        SourceText source = SourceText.From("Title\n=====\n\nBody\n");
        ConcreteSyntaxTree tree = _pack.Parse(source);
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("Title", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("# Alpha\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("# Beta\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label.Contains("Alpha"));
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label.Contains("Beta"));
    }

    [Fact]
    public void Parse_StringSafety_NoFalseHeading()
    {
        string src = "# ```\n# Real\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == "Real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseHeading()
    {
        string src = "# Title\n\n```\n<!-- fake -->\n```\n\n## Real\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        OutlineNode title = Assert.Single(outline, static o => o.Label == "Title");
        Assert.DoesNotContain(title.Children, static o => o.Label == "fake");
        Assert.Contains(title.Children, static o => o.Label == "Real");
    }
}
