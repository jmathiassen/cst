using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Python;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Python.Tests;

public class PythonLanguagePackTests : StructuralPackTestBase
{
    private readonly PythonLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "def a():\n    pass\n" + new string(' ', 100);
    protected override string FuzzSource => """
class Engine:
    def run(self):
        return 1

def main():
    return 0
""";
    protected override string BrokenSource => "def f(\n";
    protected override string[]? ExpectedBrokenDiagnosticCodes => ["PY001"];

    [Fact]
    public void Parse_Happy_NestedMethodLevels()
    {
        string src = """
class Engine:
    def run(self):
        return 1

def main():
    return 0
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);
        Assert.Equal("class", outline[0].Kind);
        Assert.Equal("Engine", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Single(outline[0].Children);
        Assert.Equal("method", outline[0].Children[0].Kind);
        Assert.Equal("run", outline[0].Children[0].Label);
        Assert.Equal(2, outline[0].Children[0].Level);
        Assert.Equal(2, outline[0].Children[0].LineSpan.Start.Line);
        Assert.Equal(3, outline[0].Children[0].LineSpan.End.Line);
        Assert.Equal("function", outline[1].Kind);
        Assert.Equal("main", outline[1].Label);
        Assert.Equal(5, outline[1].LineSpan.Start.Line);
        Assert.Equal(6, outline[1].LineSpan.End.Line);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = "class Engine:\n    pass\ndef main():\n    return 0\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode cls = tree.Root.Children[0];
        Assert.Equal("Engine", src.Substring(cls.NameSpan!.Value.Start, cls.NameSpan.Value.Length));
        SyntaxNode fn = tree.Root.Children[1];
        Assert.Equal("main", src.Substring(fn.NameSpan!.Value.Start, fn.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_StringSafety_NoFalseDef()
    {
        string src = "s = \"def fake(): pass\"\ndef real():\n    pass\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseDef()
    {
        string src = "# def fake():\npass\ndef real():\n    pass\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("def Alpha():\n    pass\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("def Beta():\n    pass\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }
}
