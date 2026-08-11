using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Swift;

namespace TgrDevelopments.Cst.Packs.Swift.Tests;

public class SwiftLanguagePackTests
{
    private readonly SwiftLanguagePack _pack = new();

    [Fact]
    public void Parse_Empty_OutlineEmpty()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        Assert.NotNull(tree.Root);
        Assert.Equal(SyntaxQuality.Structural, tree.Quality);
        Assert.Empty(_pack.GetOutline(tree));
    }

    [Fact]
    public void Parse_Happy_ClassWithMethod()
    {
        string src = """
class Engine {
    func run() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Equal("class", cls.Kind);
        Assert.Equal("Engine", cls.Label);
        Assert.Equal(1, cls.LineSpan.Start.Line);
        Assert.Equal(3, cls.LineSpan.End.Line);
        Assert.NotNull(cls.Children);
        OutlineNode method = Assert.Single(cls.Children);
        Assert.Equal("method", method.Kind);
        Assert.Equal("run", method.Label);
        Assert.Equal(2, method.LineSpan.Start.Line);
        Assert.Equal(2, method.LineSpan.End.Line);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = """
class Main {
    func run() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode cls = tree.Root.Children[0];
        Assert.Equal("Main", src.Substring(cls.NameSpan!.Value.Start, cls.NameSpan.Value.Length));
        SyntaxNode method = cls.Children[0];
        Assert.Equal("run", src.Substring(method.NameSpan!.Value.Start, method.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_StructEnumProtocol_Types()
    {
        string src = """
struct Point {
    let x: Int
}

enum Color { case red, green, blue }

protocol Drawable {
    func draw()
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "struct" && o.Label == "Point");
        Assert.Contains(outline, static o => o.Kind == "enum" && o.Label == "Color");
        Assert.Contains(outline, static o => o.Kind == "protocol" && o.Label == "Drawable");
    }

    [Fact]
    public void Parse_Extension()
    {
        string src = """
extension Engine {
    func boost() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("extension", outline[0].Kind);
        Assert.Equal("Engine", outline[0].Label);
        Assert.NotNull(outline[0].Children);
        Assert.Single(outline[0].Children);
        Assert.Equal("method", outline[0].Children[0].Kind);
        Assert.Equal("boost", outline[0].Children[0].Label);
    }

    [Fact]
    public void Parse_TopLevelFunction()
    {
        string src = """
func helper() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("function", outline[0].Kind);
        Assert.Equal("helper", outline[0].Label);
    }

    [Fact]
    public void Parse_StringSafety_NoStructure()
    {
        string src = "class Real {\n    let s = \"class Fake { func m() {} }\"\n    func real() {}\n}\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Single(cls.Children!);
        Assert.Equal("real", cls.Children![0].Label);
    }

    [Fact]
    public void Parse_MultiLineString_NoStructure()
    {
        string src = "class Real {\n    let s = \"\"\"class Fake {\n  func m() {}\n}\"\"\"\n    func real() {}\n}\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Single(cls.Children!);
        Assert.Equal("real", cls.Children![0].Label);
    }

    [Fact]
    public void Parse_CommentSafety_NoStructure()
    {
        string src = "class Real {\n    /* class Fake { func m() {} } */\n    func real() {}\n}\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Single(cls.Children!);
        Assert.Equal("real", cls.Children![0].Label);
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("class X {\n"));
        Assert.NotNull(tree.Root);
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "SWIFT001");
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "class A {}\n" + new string(' ', 100) + "\n";
        ParseOptions options = new() { MaxChars = 10 };
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(text), options);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "CST_LIMIT");
        Assert.True(tree.IsPartial);
    }

    [Fact]
    public void Parse_CancelledToken_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => _pack.Parse(
            SourceText.From("class A {}"),
            cancellationToken: cts.Token));
    }

    [Fact]
    public void Parse_NoThrowFuzz_TruncatedInputsNeverThrow()
    {
        string src = "class Engine {\n    func run() {}\n}\n";
        for (int i = 0; i < src.Length; i++)
        {
            string cut = src[..i];
            ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(cut));
            Assert.NotNull(tree.Root);
        }
    }

    [Fact]
    public void GetOutline_SharedInstance_Independent()
    {
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From("class A { func m() {} }\n"));
        ConcreteSyntaxTree empty = _pack.Parse(SourceText.From(""));
        Assert.NotEmpty(_pack.GetOutline(happy));
        Assert.Empty(_pack.GetOutline(empty));
    }

    [Fact]
    public async Task Parse_ConcurrentShared_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("class Alpha { func a() {} }\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("class Beta { func b() {} }\n"))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }
}
