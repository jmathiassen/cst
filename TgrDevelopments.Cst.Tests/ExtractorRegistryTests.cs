using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Markdown;

namespace TgrDevelopments.Cst.Tests;

public class ExtractorRegistryTests
{
    [Fact]
    public void TryExtract_NoExtractor_ReturnsNull()
    {
        LanguagePackRegistry registry = new();
        CstService cst = new(registry);
        ConcreteSyntaxTree tree = new(
            "unknown",
            SourceText.From("x"),
            new SyntaxNode("document", new TextSpan(0, 1)));

        Assert.Null(cst.TryExtract(tree));
    }

    [Fact]
    public void TryExtract_Markdown_Headings()
    {
        LanguagePackRegistry registry = new();
        MarkdownRegistration.Register(registry);
        CstService cst = new(registry);
        ConcreteSyntaxTree? tree = cst.TryParse("a.md", SourceText.From("# Hi\n"));
        StructuralExtract? extract = cst.TryExtract(tree!);
        Assert.NotNull(extract);
        Assert.Contains(extract!.Declarations, d => d.Name == "Hi");
        Assert.Equal(SyntaxConfidence.Syntax, extract.Confidence);
    }
}
