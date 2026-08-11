using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.FSharp;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.FSharp.Tests;

public class FSharpLanguagePackTests : StructuralPackTestBase
{
    private readonly FSharpLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "let x = 1\n" + new string(' ', 100);
    protected override string FuzzSource => """
module App

type Engine() =
  member _.Run() = ()

let main () = ()
""";
    protected override string BrokenSource => "type X {\n";

    [Fact]
    public void Parse_Happy_ModuleTypeMemberLet()
    {
        string src = """
module App

type Engine() =
  member _.Run() = ()

let main () = ()
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.NotEmpty(outline);
        OutlineNode app = Assert.Single(outline.Where(static o => o.Label == "App"));
        Assert.Equal("module", app.Kind);
        Assert.Equal(1, app.Level);
        Assert.Contains(app.Children, static c => c.Kind == "type" && c.Label == "Engine" && c.Level == 2);
        Assert.Contains(app.Children, static c => c is { Kind: "function" or "let", Label: "main" } && c.Level == 2);
    }

    [Fact]
    public void Parse_Nested_DoesNotSwallowSiblingModule()
    {
        string src = """
module App

type Engine() =
  member _.Run() = ()

let main () = ()

module Other
let x = 1
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.True(outline.Count >= 2, "expected App and Other as top-level");
        OutlineNode app = Assert.Single(outline, static o => o.Label == "App");
        OutlineNode other = Assert.Single(outline, static o => o.Label == "Other");
        Assert.True(app.LineSpan.End.Line < other.LineSpan.Start.Line,
            $"App To={app.LineSpan.End.Line} should end before Other From={other.LineSpan.Start.Line}");
        Assert.Contains(app.Children, static c => c.Label == "Engine");
        Assert.Contains(app.Children, static c => c.Label == "main");
        Assert.DoesNotContain(app.Children, static c => c.Label == "Other");
    }

    [Fact]
    public void Parse_StringSafety_NoFalseDecl()
    {
        string src = "let s = \"type Fake = { x: int }\"\nlet real () = ()\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseDecl()
    {
        string src = "// type Fake = { x: int }\nlet real () = ()\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == "real");
        Assert.DoesNotContain(outline, static o => o.Label == "Fake");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("let alpha () = ()\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("let beta () = ()\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "beta");
    }
}
