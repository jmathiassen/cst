using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Latex;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Latex.Tests;

public class LatexLanguagePackTests : StructuralPackTestBase
{
    private readonly LatexLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "\\section{Long}\n\\subsection{A}\n\\subsection{B}\n";
    protected override string FuzzSource => @"\section{Intro}
Text.

\subsection{Nested}
More.
";
    protected override string BrokenSource => "{ unmatched\n";

    [Fact]
    public void Parse_Happy_OutlineHasLabelsLevelsAndLines()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(@"\section{Intro}
Text.

\subsection{Nested}
More.
"));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.NotEmpty(outline);
        Assert.All(outline, static o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Kind));
            Assert.False(string.IsNullOrWhiteSpace(o.Label));
            Assert.True(o.Level >= 1);
            Assert.True(o.LineSpan.Start.Line >= 1);
            Assert.True(o.LineSpan.End.Line >= o.LineSpan.Start.Line);
            Assert.True(o.Span.Length >= 0);
        });
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_NoThrowDistinctTrees()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From(@"\section{Intro}
Text.

\subsection{Nested}
More.
"))),
            Task.Run(() => b = _pack.Parse(SourceText.From(""))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        IReadOnlyList<OutlineNode> oA = _pack.GetOutline(a!);
        IReadOnlyList<OutlineNode> oB = _pack.GetOutline(b!);
        Assert.NotEmpty(oA);
        Assert.Empty(oB);
    }
}
