using System.Linq;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Sln;

namespace TgrDevelopments.Cst.Packs.Sln.Tests;

public class SlnExtractorTests
{
    private readonly SlnLanguagePack _pack = new();
    private readonly SlnStructuralExtractor _extractor = new();

    [Fact]
    public void Extract_Empty_Empty()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        StructuralExtract extract = _extractor.Extract(tree);
        Assert.Empty(extract.Declarations);
        Assert.Empty(extract.Edges);
    }

    [Fact]
    public void Extract_Happy_SolutionProjectEdges()
    {
        string text = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "App\App.csproj", "{A1000001-0000-4000-8000-000000000001}"
            EndProject
            """;
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(text, "App.sln"));
        StructuralExtract extract = _extractor.Extract(tree);

        Assert.Contains(extract.Declarations, d => d.Kind == "project" && d.Name == "App");
        Assert.Contains(extract.Edges, e => e.Kind == "solution_project" && e.To.Contains("App.csproj"));
        Assert.Equal(SyntaxQuality.Structural, extract.Quality);
    }
}
