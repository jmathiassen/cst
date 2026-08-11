using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.TypeScript;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.TypeScript.Tests;

public class TypeScriptLanguagePackTests : StructuralPackTestBase
{
    private readonly TypeScriptLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "function a() {}\n" + new string(' ', 100);
    protected override string FuzzSource => """
interface IEngine {
  run(): void;
}
class Engine implements IEngine {
  run() {}
}
function main() {}
""";
    protected override string BrokenSource => "function f( {\n";
    protected override string[]? ExpectedBrokenDiagnosticCodes => ["TS001"];

    [Fact]
    public void Parse_Happy_InterfaceClassFunction()
    {
        string src = """
interface IEngine {
  run(): void;
}
class Engine implements IEngine {
  run() {}
}
function main() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(3, outline.Count);
        Assert.Equal("interface", outline[0].Kind);
        Assert.Equal("IEngine", outline[0].Label);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);
        Assert.Equal(3, outline[0].LineSpan.End.Line);
        Assert.Equal("class", outline[1].Kind);
        Assert.Equal("Engine", outline[1].Label);
        Assert.Equal("function", outline[2].Kind);
        Assert.Equal("main", outline[2].Label);
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
    }

    [Fact]
    public void Parse_Namespace_WalkedBodyContainsFunction()
    {
        string src = """
namespace N {
  export function f() {}
  export class C {
    m() {}
  }
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode ns = Assert.Single(outline);
        Assert.Equal("namespace", ns.Kind);
        Assert.Equal("N", ns.Label);
        Assert.NotNull(ns.Children);
        Assert.Equal(2, ns.Children.Count);
        Assert.Equal("function", ns.Children[0].Kind);
        Assert.Equal("f", ns.Children[0].Label);
        Assert.Equal("class", ns.Children[1].Kind);
        Assert.Equal("C", ns.Children[1].Label);
        Assert.NotNull(ns.Children[1].Children);
        Assert.Single(ns.Children[1].Children);
        Assert.Equal("method", ns.Children[1].Children[0].Kind);
        Assert.Equal("m", ns.Children[1].Children[0].Label);
    }

    [Fact]
    public void Parse_Module_WalkedBodyContainsFunction()
    {
        string src = """
module M {
  export function f() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode mod = Assert.Single(outline);
        Assert.Equal("namespace", mod.Kind);
        Assert.Equal("M", mod.Label);
        Assert.NotNull(mod.Children);
        Assert.Single(mod.Children);
        Assert.Equal("function", mod.Children[0].Kind);
        Assert.Equal("f", mod.Children[0].Label);
    }

    [Fact]
    public void Parse_NestedNamespace_ChildrenWalked()
    {
        string src = """
namespace Outer {
  export namespace Inner {
    export function deepest() {}
  }
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode outer = Assert.Single(outline);
        Assert.Equal("Outer", outer.Label);
        Assert.NotNull(outer.Children);
        OutlineNode inner = Assert.Single(outer.Children);
        Assert.Equal("Inner", inner.Label);
        Assert.NotNull(inner.Children);
        Assert.Single(inner.Children);
        Assert.Equal("deepest", inner.Children[0].Label);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = "function add(x: number): number { return x; }\nclass Engine { run() {} }\n";
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
    public void Parse_TemplateLiteralSafety_NoFalseFunction()
    {
        string src = """
const s = `function fake() {}`;
const t = `${function alsoFake() {}}`;
function real() {}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.DoesNotContain(outline, static o => o.Label == "alsoFake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("function Alpha() {}\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("function Beta() {}\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }
}
