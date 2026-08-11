using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Json;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Json.Tests;

public class JsonPackTests : StructuralPackTestBase
{
    private readonly JsonLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => """
{
  "name": "cst",
  "packs": ["markdown", "csv"],
  "meta": {
    "version": 1
  }
}
""";
    protected override string FuzzSource => """
{
  "name": "cst",
  "packs": ["markdown", "csv"],
  "meta": {
    "version": 1
  }
}
""";
    protected override string BrokenSource => """{ "a": """;
    [Fact]
    public void Parse_Happy_NestedObjectAndArray()
    {
        SourceText source = SourceText.From("""
            {
              "name": "cst",
              "packs": ["markdown", "csv"],
              "meta": {
                "version": 1
              }
            }
            """);
        ConcreteSyntaxTree tree = _pack.Parse(source);
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("object", outline[0].Kind);
        Assert.Contains(outline[0].Children, c => c.Kind == "key" && c.Label == "name");
        Assert.Contains(outline[0].Children, c => c.Kind == "key" && c.Label == "packs");
        Assert.Contains(outline[0].Children, c => c.Kind == "key" && c.Label == "meta");
        OutlineNode packs = outline[0].Children.First(c => c.Label == "packs");
        Assert.Contains(packs.Children, c => c.Kind == "array");
    }

    [Fact]
    public void Parse_Broken_PartialWithDiagnostic()
    {
        SourceText source = SourceText.From("""{ "a": """);
        ConcreteSyntaxTree tree = _pack.Parse(source);
        Assert.Contains(tree.Diagnostics, d => d.Code == "JSON001");
        Assert.NotNull(tree.Root);
    }

    [Fact]
    public void Parse_TrailingComma_Warning()
    {
        SourceText source = SourceText.From("""{ "a": 1, }""");
        ConcreteSyntaxTree tree = _pack.Parse(source);
        Assert.Contains(tree.Diagnostics, d => d.Code == "JSON002");
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline[0].Children, c => c.Label == "a");
    }

    [Fact]
    public void Parse_Jsonc_AllowsComments()
    {
        SourceText source = SourceText.From("""
            {
              // comment
              "a": 1
            }
            """, "config.jsonc");
        ConcreteSyntaxTree tree = _pack.Parse(source);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == "JSON003");
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline[0].Children, c => c.Label == "a");
    }

    [Fact]
    public void Parse_CommentInJson_Warning()
    {
        SourceText source = SourceText.From("""
            {
              // comment
              "a": 1
            }
            """, "config.json");
        ConcreteSyntaxTree tree = _pack.Parse(source);
        Assert.Contains(tree.Diagnostics, d => d.Code == "JSON003");
    }

    [Fact]
    public void Parse_ScalarRoot_ValueOutline()
    {
        SourceText source = SourceText.From("42");
        ConcreteSyntaxTree tree = _pack.Parse(source);
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("value", outline[0].Kind);
        Assert.Equal("42", outline[0].Label);
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("""{ "alpha": 1 }"""))),
            Task.Run(() => b = _pack.Parse(SourceText.From("""{ "beta": 2 }"""))));
        IReadOnlyList<OutlineNode> oA = _pack.GetOutline(a!);
        IReadOnlyList<OutlineNode> oB = _pack.GetOutline(b!);
        Assert.Contains(oA[0].Children, static c => c.Label == "alpha");
        Assert.Contains(oB[0].Children, static c => c.Label == "beta");
    }

    [Fact]
    public void Parse_StringSafety_NoFalseKey()
    {
        string src = """{ "a": "b: c", "real": 1 }""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline[0].Children, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseKey()
    {
        string src = """
// { "fake": 1 }
{ "real": 1 }
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline[0].Children, static o => o.Label == "fake");
        Assert.Contains(outline[0].Children, static o => o.Label == "real");
    }
}
