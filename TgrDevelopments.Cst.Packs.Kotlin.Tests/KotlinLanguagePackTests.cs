using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Kotlin;

namespace TgrDevelopments.Cst.Packs.Kotlin.Tests;

public class KotlinLanguagePackTests
{
    private readonly KotlinLanguagePack _pack = new();

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
    public void Parse_Happy_PackageClassFun()
    {
        string src = """
package com.demo

class Engine {
    fun run(): Unit {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);

        Assert.Equal("package", outline[0].Kind);
        Assert.Equal("com.demo", outline[0].Label);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);
        Assert.Equal(1, outline[0].LineSpan.End.Line);

        OutlineNode cls = outline[1];
        Assert.Equal("class", cls.Kind);
        Assert.Equal("Engine", cls.Label);
        Assert.Equal(3, cls.LineSpan.Start.Line);
        Assert.Equal(5, cls.LineSpan.End.Line);
        Assert.NotNull(cls.Children);
        OutlineNode method = Assert.Single(cls.Children);
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
package app

class Main {
    fun run() {}
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
    public void Parse_NestedClass_InnerTypeAndFun()
    {
        string src = """
class Outer {
    class Inner {
        fun innerFun() {}
    }
    fun outerFun() {}
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
        Assert.Single(inner.Children);
        Assert.Equal("method", inner.Children[0].Kind);
        Assert.Equal("innerFun", inner.Children[0].Label);

        Assert.Equal("method", outer.Children[1].Kind);
        Assert.Equal("outerFun", outer.Children[1].Label);
    }

    [Fact]
    public void Parse_TopLevelFunctions()
    {
        string src = """
package app

fun helper() {}

fun process(input: String): Int { return 1 }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "fun" && o.Label == "helper");
        Assert.Contains(outline, static o => o.Kind == "fun" && o.Label == "process");
    }

    [Fact]
    public void Parse_InterfaceObject_Types()
    {
        string src = """
interface Drawable {
    fun draw()
}

object Singleton {
    val name = "app"
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "interface" && o.Label == "Drawable");
        Assert.Contains(outline, static o => o.Kind == "object" && o.Label == "Singleton");
    }

    [Fact]
    public void Parse_DataClass_OutlinedWithMethods()
    {
        string src = """
package app

data class User(val name: String, val age: Int)
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = outline.First(o => o.Kind == "class");
        Assert.Equal("User", cls.Label);
        // data class auto-methods (componentN, copy, toString, etc.) should not produce false outlines
        Assert.NotNull(cls.Children);
        Assert.Empty(cls.Children);
    }

    [Fact]
    public void Parse_StringSafety_KeywordsInStringsNoStructure()
    {
        string src = """
class Real {
    val s = "class Fake { fun method() {} }"
    fun real() {}
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
    public void Parse_RawString_NoStructure()
    {
        string src = "class Real {\n    val s = \"\"\"\n        class Fake { }\n        \"\"\";\n    fun real() {}\n}\n";
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
    fun real() {}
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
        Assert.Contains(tree.Diagnostics, static d => d.Code == "KOTLIN001");
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "package x\n" + new string(' ', 100) + "\n";
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
package app

class Engine {
    fun run() {}
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
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From("class A { fun m() {} }\n"));
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
            Task.Run(() => a = _pack.Parse(SourceText.From("class Alpha { fun a() {} }\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("class Beta { fun b() {} }\n"))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }
}
