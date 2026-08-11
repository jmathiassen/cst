using System.Linq;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Markdown;

namespace TgrDevelopments.Cst.Packs.Markdown.Tests;

public class MarkdownExtractorTests
{
    private readonly MarkdownLanguagePack _pack = new();
    private readonly MarkdownStructuralExtractor _extractor = new();

    [Fact]
    public void Extract_Empty_Empty()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        StructuralExtract extract = _extractor.Extract(tree);
        Assert.Empty(extract.Declarations);
        Assert.Empty(extract.Calls);
    }

    [Fact]
    public void Extract_Happy_Headings()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("# Title\n\n## Section\n"));
        StructuralExtract extract = _extractor.Extract(tree);

        Assert.Contains(extract.Declarations, d => d.Kind == "heading" && d.Name == "Title");
        Assert.Contains(extract.Declarations, d => d.Kind == "heading" && d.Name == "Section");
        Assert.Equal(SyntaxQuality.Preview, extract.Quality);
    }
}
