using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Csv;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Csv.Tests;

public class CsvPackTests : StructuralPackTestBase
{
    private readonly CsvLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "a,b,c,d,e,f,g\n1,2,3,4,5,6,7\n";
    protected override string FuzzSource => "name,age,city\nAda,36,London\nBob,41,Paris\n";
    protected override string BrokenSource => "\"unclosed\n";

    [Fact]
    public void Parse_Happy_HeaderAndColumns()
    {
        SourceText source = SourceText.From("name,age,city\nAda,36,London\nBob,41,Paris\n", "data.csv");
        ConcreteSyntaxTree tree = _pack.Parse(source);
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode header = Assert.Single(outline, o => o.Kind == "header");
        Assert.Equal(1, header.Level);
        Assert.Equal(3, header.Children.Count);
        Assert.Contains(header.Children, c => c.Kind == "column" && c.Label == "name");
        Assert.Contains(header.Children, c => c.Kind == "column" && c.Label == "age");
        Assert.Contains(header.Children, c => c.Kind == "column" && c.Label == "city");
        Assert.Contains(outline, o => o.Kind == "row" && o.Label == "Ada");
        Assert.Contains(outline, o => o.Kind == "row" && o.Label == "Bob");
    }

    [Fact]
    public void Parse_Broken_UnclosedQuote_Diagnostic()
    {
        SourceText source = SourceText.From("a,b\n\"unclosed,c\n", "data.csv");
        ConcreteSyntaxTree tree = _pack.Parse(source);
        Assert.Contains(tree.Diagnostics, d => d.Code == "CSV002");
    }

    [Fact]
    public void Parse_Tsv_UsesTabDelimiter()
    {
        SourceText source = SourceText.From("a\tb\nx\ty\n", "data.tsv");
        ConcreteSyntaxTree tree = _pack.Parse(source);
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode header = Assert.Single(outline, o => o.Kind == "header");
        Assert.Equal(2, header.Children.Count);
        Assert.Equal("a", header.Children[0].Label);
        Assert.Equal("b", header.Children[1].Label);
    }

    [Fact]
    public void Parse_RaggedRow_Warning()
    {
        SourceText source = SourceText.From("a,b,c\n1,2\n", "data.csv");
        ConcreteSyntaxTree tree = _pack.Parse(source);
        Assert.Contains(tree.Diagnostics, d => d.Code == "CSV001");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("a,b\n1,2\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From(""))));
        Assert.NotEmpty(_pack.GetOutline(a!));
        Assert.Empty(_pack.GetOutline(b!));
    }
}
