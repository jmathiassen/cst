using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Sln;

namespace TgrDevelopments.Cst.Packs.Sln.Tests;

public class SlnLanguagePackTests
{
    private readonly SlnLanguagePack _pack = new();

    private const string HappySln = """
Microsoft Visual Studio Solution File, Format Version 12.00
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "App\App.csproj", "{A1000001-0000-4000-8000-000000000001}"
EndProject
""";

    [Fact]
    public void Parse_Empty_OutlineEmpty()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        Assert.NotNull(tree.Root);
        Assert.Equal(SyntaxConfidence.Syntax, tree.Confidence);
        Assert.Empty(_pack.GetOutline(tree));
    }

    [Fact]
    public void Parse_Happy_OutlineHasLabelsLevelsAndLines()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(HappySln, "App.sln"));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.NotEmpty(outline);
        Assert.Contains(outline, static o => o.Label.Contains("App"));
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

    private const string HappySlnx = """
<Solution>
  <Folder Name="/src/">
    <Project Path="src/App/App.csproj" />
  </Folder>
  <Project Path="tests/Tests.csproj" />
</Solution>
""";

    [Fact]
    public void Parse_Slnx_Happy_OutlineProjectsAndFoldersWithNesting()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(HappySlnx, "App.slnx"));
        Assert.Equal("document", tree.Root.Kind);
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);
        Assert.All(outline, static o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Kind));
            Assert.False(string.IsNullOrWhiteSpace(o.Label));
            Assert.True(o.LineSpan.Start.Line >= 1);
            Assert.True(o.Span.Length >= 0);
        });

        OutlineNode folder = outline.First(o => o.Kind == "folder");
        Assert.Equal("/src/", folder.Label);
        Assert.Equal(1, folder.Level);
        OutlineNode app = Assert.Single(folder.Children);
        Assert.Equal("project", app.Kind);
        Assert.Equal("App.csproj", app.Label);
        Assert.Equal(2, app.Level);

        OutlineNode tests = outline.First(o => o.Kind == "project" && o.Label == "Tests.csproj");
        Assert.Equal(1, tests.Level);
    }

    [Fact]
    public void Parse_Slnx_Happy_NameSpanPointsAtIdentifier()
    {
        string src = """
<Solution>
  <Project Path="src/App/App.csproj" />
  <Folder Name="/src/">
    <Project Path="src/Lib/Lib.csproj" />
  </Folder>
</Solution>
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src, "App.slnx"));
        SyntaxNode app = tree.Root.Children[0];
        Assert.Equal("App.csproj", src.Substring(app.NameSpan!.Value.Start, app.NameSpan.Value.Length));
        SyntaxNode folder = tree.Root.Children[1];
        Assert.Equal("/src/", src.Substring(folder.NameSpan!.Value.Start, folder.NameSpan.Value.Length));
        SyntaxNode lib = folder.Children[0];
        Assert.Equal("Lib.csproj", src.Substring(lib.NameSpan!.Value.Start, lib.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_Slnx_Broken_MalformedXmlYieldsSln004NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(
            "<Solution><Folder Name=\"/src/\"><Project Path=\"App.csproj\">\n</Folder></Solution>",
            "App.slnx"));
        Assert.NotNull(tree.Root);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "SLN004");
        Assert.True(tree.IsPartial);
    }

    [Fact]
    public void Parse_Slnx_AttributeSafety_QuotesAndGtInValues()
    {
        string src = """
<Solution>
  <Project Path='app">x.csproj' />
  <Project Path="a>b.csproj" />
</Solution>
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src, "App.slnx"));
        Assert.DoesNotContain(tree.Diagnostics, static d => d.Code == "SLN004");
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);
        Assert.All(outline, static o => Assert.Equal(1, o.Level));
    }

    [Fact]
    public void Parse_Slnx_SniffedWithoutExtension_UsesXmlBranch()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(HappySlnx));
        Assert.Equal("document", tree.Root.Kind);
        Assert.NotEmpty(_pack.GetOutline(tree));
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("Project( broken\n"));
        Assert.NotNull(tree.Root);
    }

    [Fact]
    public void GetOutline_SharedInstance_IndependentOfLaterParse()
    {
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From(HappySln, "App.sln"));
        ConcreteSyntaxTree empty = _pack.Parse(SourceText.From(""));
        IReadOnlyList<OutlineNode> emptyOutline = _pack.GetOutline(empty);
        IReadOnlyList<OutlineNode> happyOutline = _pack.GetOutline(happy);
        Assert.Empty(emptyOutline);
        Assert.NotEmpty(happyOutline);
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_NoThrowDistinctTrees()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From(HappySln, "App.sln"))),
            Task.Run(() => b = _pack.Parse(SourceText.From(""))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        IReadOnlyList<OutlineNode> oA = _pack.GetOutline(a!);
        IReadOnlyList<OutlineNode> oB = _pack.GetOutline(b!);
        Assert.NotEmpty(oA);
        Assert.Empty(oB);
    }
}
