using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.JavaScript;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.JavaScript.Tests;

public class JavaScriptLanguagePackTests : StructuralPackTestBase
{
    private readonly JavaScriptLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "function a() {}\n" + new string(' ', 100);
    protected override string FuzzSource => """
class Engine {
  run() {
    return 1;
  }
}
function main() {
  return 0;
}
""";
    protected override string BrokenSource => "function f( {\n";
    protected override string[]? ExpectedBrokenDiagnosticCodes => ["JS001"];

    [Fact]
    public void Parse_Happy_ClassAndFunction()
    {
        string src = """
class Engine {
  run() {
    return 1;
  }
}
function main() {
  return 0;
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);
        Assert.Equal("class", outline[0].Kind);
        Assert.Equal("Engine", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);
        Assert.Equal(5, outline[0].LineSpan.End.Line);
        Assert.Contains(outline[0].Children, static c => c.Kind == "method" && c.Label == "run" && c.Level == 2);
        Assert.Equal("function", outline[1].Kind);
        Assert.Equal("main", outline[1].Label);
        Assert.Equal(6, outline[1].LineSpan.Start.Line);
        Assert.Equal(8, outline[1].LineSpan.End.Line);
    }

    [Fact]
    public void Parse_Nested_ClassContainsMethod()
    {
        string src = """
class Outer {
  methodA() {}
  methodB() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Equal("class", cls.Kind);
        Assert.Equal("Outer", cls.Label);
        Assert.NotNull(cls.Children);
        Assert.Equal(2, cls.Children.Count);
        Assert.Equal("method", cls.Children[0].Kind);
        Assert.Equal("methodA", cls.Children[0].Label);
        Assert.Equal(2, cls.Children[0].Level);
        Assert.True(cls.Children[0].LineSpan.Start.Line >= cls.LineSpan.Start.Line);
        Assert.True(cls.Children[0].LineSpan.End.Line <= cls.LineSpan.End.Line);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = "function add(x, y) { return x + y; }\nclass Engine { run() {} }\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode fn = tree.Root.Children[0];
        Assert.Equal("add", src.Substring(fn.NameSpan!.Value.Start, fn.NameSpan.Value.Length));
        SyntaxNode cls = tree.Root.Children[1];
        Assert.Equal("Engine", src.Substring(cls.NameSpan!.Value.Start, cls.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_StringSafety_NoFalseFunction()
    {
        string src = "const s = \"function fake() {}\";\nfunction real() {}\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseFunction()
    {
        string src = "// function fake() {}\nfunction real() {}\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_ObjectLiteral_MethodsOutlined()
    {
        string src = """
const api = {
  run() { return 1; },
  stop() { return 0; }
};
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        OutlineNode obj = Assert.Single(outline);
        Assert.Equal("object", obj.Kind);
        Assert.Equal("api", obj.Label);
        Assert.Contains(obj.Children, static c => c.Kind == "method" && c.Label == "run");
        Assert.Contains(obj.Children, static c => c.Kind == "method" && c.Label == "stop");
    }

    [Fact]
    public void Parse_Class_PrivateFields()
    {
        string src = """
class Box {
  #value = 0;
  getValue() { return this.#value; }
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        OutlineNode cls = Assert.Single(outline);
        Assert.Equal("class", cls.Kind);
        Assert.Contains(cls.Children, static c => c.Kind == "field" && c.Label == "#value");
        Assert.Contains(cls.Children, static c => c.Kind == "method" && c.Label == "getValue");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("function Alpha() {}\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("function Beta() {}\n"))));
        IReadOnlyList<OutlineNode> oA = _pack.GetOutline(a!);
        IReadOnlyList<OutlineNode> oB = _pack.GetOutline(b!);
        Assert.Contains(oA, static o => o.Label == "Alpha");
        Assert.Contains(oB, static o => o.Label == "Beta");
    }
}
