using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Java;

namespace TgrDevelopments.Cst.Packs.Java.Tests;

public class JavaLanguagePackTests
{
    private readonly JavaLanguagePack _pack = new();

    [Fact]
    public void Parse_Empty_OutlineEmpty()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        Assert.NotNull(tree.Root);
        Assert.Equal(SyntaxConfidence.Syntax, tree.Confidence);
        Assert.Equal(SyntaxQuality.Structural, tree.Quality);
        Assert.Empty(_pack.GetOutline(tree));
    }

    [Fact]
    public void Parse_Happy_PackageClassMethod()
    {
        string src = """
package com.demo;

public class Engine {
    public void run() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);

        Assert.Equal("package", outline[0].Kind);
        Assert.Equal("com.demo", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);
        Assert.Equal(1, outline[0].LineSpan.End.Line);

        Assert.Equal("class", outline[1].Kind);
        Assert.Equal("Engine", outline[1].Label);
        Assert.Equal(1, outline[1].Level);
        Assert.Equal(3, outline[1].LineSpan.Start.Line);
        Assert.Equal(5, outline[1].LineSpan.End.Line);
        Assert.NotNull(outline[1].Children);

        OutlineNode method = Assert.Single(outline[1].Children);
        Assert.Equal("method", method.Kind);
        Assert.Equal("run", method.Label);
        Assert.Equal(2, method.Level);
        Assert.Equal(4, method.LineSpan.Start.Line);
        Assert.Equal(4, method.LineSpan.End.Line);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = """
package app;

class Main {
    void run() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode pkg = tree.Root.Children[0];
        Assert.Equal("app", src.Substring(pkg.NameSpan!.Value.Start, pkg.NameSpan.Value.Length));
        SyntaxNode cls = tree.Root.Children[1];
        Assert.Equal("Main", src.Substring(cls.NameSpan!.Value.Start, cls.NameSpan.Value.Length));
        SyntaxNode method = cls.Children[0];
        Assert.Equal("run", src.Substring(method.NameSpan!.Value.Start, method.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_InterfaceEnumRecord_Types()
    {
        string src = """
interface Drawable {
    void draw();
}

enum Color { RED, GREEN, BLUE }

record Point(int x, int y) {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "interface" && o.Label == "Drawable");
        Assert.Contains(outline, static o => o.Kind == "enum" && o.Label == "Color");
        Assert.Contains(outline, static o => o.Kind == "record" && o.Label == "Point");
    }

    [Fact]
    public void Parse_SealedClass_PermitsSubclassOutlined()
    {
        string src = """
sealed class Shape permits Circle, Square {}

final class Circle extends Shape {}

final class Square extends Shape {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(3, outline.Count);
        Assert.Equal("Shape", outline[0].Label);
        Assert.Equal("Circle", outline[1].Label);
        Assert.Equal("Square", outline[2].Label);
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
        Assert.Equal("class", outer.Kind);
        Assert.Equal("Outer", outer.Label);
        Assert.NotNull(outer.Children);
        Assert.Equal(2, outer.Children.Count);

        OutlineNode inner = outer.Children[0];
        Assert.Equal("class", inner.Kind);
        Assert.Equal("Inner", inner.Label);
        Assert.NotNull(inner.Children);
        Assert.Equal("method", Assert.Single(inner.Children).Kind);
        Assert.Equal("innerMethod", Assert.Single(inner.Children).Label);

        Assert.Equal("method", outer.Children[1].Kind);
        Assert.Equal("outerMethod", outer.Children[1].Label);
    }

    [Fact]
    public void Parse_StringSafety_KeywordsInStringsNoStructure()
    {
        string src = """
class Real {
    String s = "class Fake { void method() {} }";
    void real() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Equal("Real", cls.Label);
        Assert.Single(cls.Children!);
        Assert.Equal("real", cls.Children![0].Label);
    }

    [Fact]
    public void Parse_TextBlock_NoStructure()
    {
        string src = "class Real {\n    String sql = \"\"\"\n        SELECT * FROM fake\n        \"\"\";\n    void real() {}\n}\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Equal("Real", cls.Label);
        OutlineNode method = Assert.Single(cls.Children!);
        Assert.Equal("real", method.Label);
    }

    [Fact]
    public void Parse_CommentSafety_KeywordsInCommentsNoStructure()
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
        Assert.Contains(tree.Diagnostics, static d => d.Code == "JAVA001");
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "package x;\n" + new string(' ', 100) + "\n";
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
        string src = """
package app;

class Engine {
    void run() {}
}
""";
        for (int i = 0; i < src.Length; i++)
        {
            string cut = src[..i];
            ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(cut));
            Assert.NotNull(tree.Root);
        }
    }

    [Fact]
    public void GetOutline_SharedInstance_IndependentOfLaterParse()
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
