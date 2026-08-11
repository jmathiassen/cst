using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.CSharpPreview;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.CSharpPreview.Tests;

public class CSharpPreviewLanguagePackTests : StructuralPackTestBase
{
    private readonly CSharpPreviewLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "class A {}\n" + new string(' ', 100);
    protected override string FuzzSource => """
namespace Demo;

public class Engine
{
    public void Run() {}
}
""";
    protected override string BrokenSource => "class X {\n";
    protected override string[]? ExpectedBrokenDiagnosticCodes => ["CS001"];

    [Fact]
    public void Parse_Happy_NestedNamespaceClassMethod()
    {
        string src = """
namespace Demo;

public class Engine
{
    public void Run() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "namespace" && o.Label == "Demo");
        OutlineNode? cls = outline.FirstOrDefault(static o => o.Kind == "class" && o.Label == "Engine")
            ?? outline.SelectMany(static o => o.Children).FirstOrDefault(static o => o.Kind == "class");
        Assert.NotNull(cls);
        Assert.Equal("Engine", cls!.Label);
        Assert.Equal(3, cls.LineSpan.Start.Line);
        Assert.Equal(6, cls.LineSpan.End.Line);
        Assert.Contains(cls.Children, static o => o.Kind == "method" && o.Label == "Run");
        OutlineNode method = cls.Children.First(static o => o.Label == "Run");
        Assert.Equal(cls.Level + 1, method.Level);
        Assert.Equal(5, method.LineSpan.Start.Line);
        Assert.Equal(5, method.LineSpan.End.Line);
    }

    [Fact]
    public void Parse_ClassMembers_PropertiesEventsIndexers()
    {
        string src = """
namespace N {
  public class C {
    public int Prop { get; set; }
    public int Expr => 1;
    public int this[int i] => i;
    public event System.EventHandler? E;
    public void Run() {}
  }
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        OutlineNode cls = Flatten(outline).First(static o => o.Kind == "class" && o.Label == "C");
        Assert.Contains(cls.Children, static o => o.Kind == "property" && o.Label == "Prop");
        Assert.Contains(cls.Children, static o => o.Kind == "property" && o.Label == "Expr");
        Assert.Contains(cls.Children, static o => o.Kind == "indexer" && o.Label == "this");
        Assert.Contains(cls.Children, static o => o.Kind == "event" && o.Label == "E");
        Assert.Contains(cls.Children, static o => o.Kind == "method" && o.Label == "Run");
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = "class Engine { void Run() {} }\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode cls = tree.Root.Children[0];
        Assert.Equal("Engine", src.Substring(cls.NameSpan!.Value.Start, cls.NameSpan.Value.Length));
        SyntaxNode method = cls.Children[0];
        Assert.Equal("Run", src.Substring(method.NameSpan!.Value.Start, method.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_StringSafety_NoFalseClass()
    {
        string src = "class Real { string s = \"class Fake {}\"; }\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(Flatten(outline), static o => o.Label == "Fake");
        Assert.Contains(Flatten(outline), static o => o.Label == "Real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseClass()
    {
        string src = "// class Fake {}\nclass Real {}\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(Flatten(outline), static o => o.Label == "Fake");
        Assert.Contains(Flatten(outline), static o => o.Label == "Real");
    }

    [Fact]
    public void Parse_RawStringLiteral_NoFalseClass()
    {
        string src = "class Real {\n    string s = \"\"\"\n        hello\n        \"\"\";\n    void Run() {}\n}\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(Flatten(outline), static o => o.Label == "Real");
        Assert.Contains(Flatten(outline), static o => o.Label == "Run");
        Assert.DoesNotContain(Flatten(outline), static o => o.Label == "hello");
    }

    [Fact]
    public void Parse_Operator_Outlined()
    {
        string src = "class C { public static C operator +(C a, C b) { return a; } }\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(Flatten(outline), static o => o.Kind == "operator" && o.Label == "+");
    }

    [Fact]
    public void Parse_TopLevelLocalFunction_Outlined()
    {
        string src = "void Local() { }\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "function" && o.Label == "Local");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("class Alpha {}\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("class Beta {}\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }

    private static IEnumerable<OutlineNode> Flatten(IEnumerable<OutlineNode> nodes)
    {
        foreach (OutlineNode n in nodes)
        {
            yield return n;
            foreach (OutlineNode c in Flatten(n.Children))
                yield return c;
        }
    }
}
