using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Odin;

namespace TgrDevelopments.Cst.Packs.Odin.Tests;

public class OdinLanguagePackTests
{
    private readonly OdinLanguagePack _pack = new();

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
    public void Parse_Happy_WorkedExample_PackageStructProc()
    {
        string src = """
package main

Vec2 :: struct {
  x, y: f32,
}

add :: proc(a, b: Vec2) -> Vec2 {
  return a
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(3, outline.Count);

        Assert.Equal("package", outline[0].Kind);
        Assert.Equal("main", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);

        Assert.Equal("struct", outline[1].Kind);
        Assert.Equal("Vec2", outline[1].Label);
        Assert.Equal(3, outline[1].LineSpan.Start.Line);

        Assert.Equal("proc", outline[2].Kind);
        Assert.Equal("add", outline[2].Label);
        Assert.Equal(7, outline[2].LineSpan.Start.Line);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = """
package app

add :: proc(x: int) -> int { return x }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode pkg = tree.Root.Children[0];
        Assert.Equal("app", src.Substring(pkg.NameSpan!.Value.Start, pkg.NameSpan.Value.Length));
        SyntaxNode procNode = tree.Root.Children[1];
        Assert.Equal("add", src.Substring(procNode.NameSpan!.Value.Start, procNode.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_EnumUnion_Types()
    {
        string src = """
package app

Color :: enum { Red, Green, Blue }

Data :: union { int; float; }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "enum" && o.Label == "Color");
        Assert.Contains(outline, static o => o.Kind == "union" && o.Label == "Data");
    }

    [Fact]
    public void Parse_ConstVar_TopLevel()
    {
        string src = """
package app

MAX_SIZE :: 100

name : string = "test"
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "const" && o.Label == "MAX_SIZE");
        Assert.Contains(outline, static o => o.Kind == "const" && o.Label == "name");
    }

    [Fact]
    public void Parse_StringSafety_KeywordsInStringsNoStructure()
    {
        string src = """
package app

let _ = "proc fake() {}"
let _ = `struct AlsoFake { }`

real :: proc() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        IReadOnlyList<OutlineNode> procs = outline.Where(static o => o.Kind == "proc").ToList();
        Assert.Single(procs);
        Assert.Equal("real", procs[0].Label);
    }

    [Fact]
    public void Parse_CommentSafety_KeywordsInCommentsNoStructure()
    {
        string src = """
package app

// proc fake() {}
/* struct also_fake {} */
real :: proc() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        IReadOnlyList<OutlineNode> procs = outline.Where(static o => o.Kind == "proc").ToList();
        Assert.Single(procs);
        Assert.Equal("real", procs[0].Label);
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("add :: proc( {\n"));
        Assert.NotNull(tree.Root);
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "ODIN001");
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "package main\n" + new string(' ', 100) + "\n";
        ParseOptions options = new() { MaxChars = 12 };
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
            SourceText.From("package main"),
            cancellationToken: cts.Token));
    }

    [Fact]
    public void Parse_NoThrowFuzz_TruncatedInputsNeverThrow()
    {
        string src = """
package main

Vec2 :: struct { x, y: f32 }

add :: proc(a, b: Vec2) -> Vec2 { return a }
""";
        for (int i = 0; i < src.Length; i++)
        {
            string cut = src[..i];
            try
            {
                ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(cut));
                Assert.NotNull(tree.Root);
            }
            catch
            {
                throw new InvalidOperationException($"Fuzz failed at prefix {i}: '{cut}'");
            }
        }
    }

    [Fact]
    public void GetOutline_SharedInstance_IndependentOfLaterParse()
    {
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From("package main\nadd :: proc() {}\n"));
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
            Task.Run(() => a = _pack.Parse(SourceText.From("package a\nadd :: proc() {}\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("package b\nsub :: proc() {}\n"))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "add");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "sub");
    }
}
