using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Dart;

namespace TgrDevelopments.Cst.Packs.Dart.Tests;

public class DartLanguagePackTests
{
    private readonly DartLanguagePack _pack = new();

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
    void run() {}
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
        Assert.Equal(2, method.Level);
        Assert.Equal(2, method.LineSpan.Start.Line);
        Assert.Equal(2, method.LineSpan.End.Line);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = """
class Main {
    void run() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode cls = tree.Root.Children[0];
        Assert.Equal("Main", src.Substring(cls.NameSpan!.Value.Start, cls.NameSpan.Value.Length));
        SyntaxNode method = cls.Children[0];
        Assert.Equal("run", src.Substring(method.NameSpan!.Value.Start, method.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_MixinEnum_Types()
    {
        string src = """
mixin Walkable {
    void walk() {}
}

enum Color { red, green, blue }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "mixin" && o.Label == "Walkable");
        Assert.Contains(outline, static o => o.Kind == "enum" && o.Label == "Color");
    }

    [Fact]
    public void Parse_NestedClass_InnerTypeAndMethod()
    {
        string src = """
class Outer {
    class Inner {
        void innerMethod() {}
    }
    void outerMethod() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode outer = Assert.Single(outline);
        Assert.Equal(2, outer.Children!.Count);
        Assert.Equal("class", outer.Children[0].Kind);
        Assert.Equal("Inner", outer.Children[0].Label);
        Assert.Equal("method", outer.Children[1].Kind);
        Assert.Equal("outerMethod", outer.Children[1].Label);
    }

    [Fact]
    public void Parse_TopLevelFunction()
    {
        string src = """
int add(int a, int b) => a + b;

void helper() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "method" && o.Label == "add");
        Assert.Contains(outline, static o => o.Kind == "method" && o.Label == "helper");
    }

    [Fact]
    public void Parse_StringSafety_KeywordsInStringsNoStructure()
    {
        string src = "class Real {\n    var s = \"class Fake { void m() {} }\";\n    void real() {}\n}\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Single(cls.Children!);
        Assert.Equal("real", cls.Children![0].Label);
    }

    [Fact]
    public void Parse_StringSafety_RawStringsNoStructure()
    {
        string src = "class Real {\n    var s = r\"class Fake { void m() {} }\";\n    void real() {}\n}\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Single(cls.Children!);
        Assert.Equal("real", cls.Children![0].Label);
    }

    [Fact]
    public void Parse_MultiLineString_NoStructure()
    {
        string src = "class Real {\n    var s = \"\"\"class Fake {\n  void m() {}\n}\"\"\";\n    void real() {}\n}\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Single(cls.Children!);
        Assert.Equal("real", cls.Children![0].Label);
    }

    [Fact]
    public void Parse_CommentSafety_NoStructure()
    {
        string src = """
class Real {
    // class Fake {}
    /* class AlsoFake {} */
    void real() {}
}
""";
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
        Assert.Contains(tree.Diagnostics, static d => d.Code == "DART001");
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
        string src = "class Engine {\n    void run() {}\n}\n";
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
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From("class A { void m() {} }\n"));
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
            Task.Run(() => a = _pack.Parse(SourceText.From("class Alpha { void a() {} }\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("class Beta { void b() {} }\n"))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }
}
