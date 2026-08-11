using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Zig;

namespace TgrDevelopments.Cst.Packs.Zig.Tests;

public class ZigLanguagePackTests
{
    private readonly ZigLanguagePack _pack = new();

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
    public void Parse_Happy_WorkedExample_StructWithMethodAndFn()
    {
        string src = """
pub const App = struct {
    pub fn run(self: *App) void {}
};

pub fn main() void {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);

        OutlineNode structNode = outline[0];
        Assert.Equal("struct", structNode.Kind);
        Assert.Equal("App", structNode.Label);
        Assert.Equal(1, structNode.Level);
        Assert.Equal(1, structNode.LineSpan.Start.Line);
        Assert.Equal(3, structNode.LineSpan.End.Line);
        Assert.NotNull(structNode.Children);
        OutlineNode methodNode = Assert.Single(structNode.Children);
        Assert.Equal("method", methodNode.Kind);
        Assert.Equal("run", methodNode.Label);
        Assert.Equal(2, methodNode.Level);
        Assert.Equal(2, methodNode.LineSpan.Start.Line);
        Assert.Equal(2, methodNode.LineSpan.End.Line);

        OutlineNode fnNode = outline[1];
        Assert.Equal("fn", fnNode.Kind);
        Assert.Equal("main", fnNode.Label);
        Assert.Equal(1, fnNode.Level);
        Assert.Equal(5, fnNode.LineSpan.Start.Line);
        Assert.Equal(6, fnNode.LineSpan.End.Line);
    }

    [Fact]
    public void Parse_Nested_StructContainsMethod_TopLevelFn()
    {
        string src = """
pub const Container = struct {
    fn alpha(self: *Container) void {}
    fn beta(self: *Container) void {}
};

pub fn top() void {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);

        OutlineNode container = outline[0];
        Assert.Equal("struct", container.Kind);
        Assert.Equal("Container", container.Label);
        Assert.Equal(1, container.Level);
        Assert.NotNull(container.Children);
        Assert.Equal(2, container.Children.Count);
        Assert.Equal("method", container.Children[0].Kind);
        Assert.Equal("alpha", container.Children[0].Label);
        Assert.Equal("method", container.Children[1].Kind);
        Assert.Equal("beta", container.Children[1].Label);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = """
pub fn add(a: i32, b: i32) i32 { return a + b; }

pub const Point = struct { x: i32, y: i32 };
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode fnNode = tree.Root.Children[0];
        Assert.Equal("add", src.Substring(fnNode.NameSpan!.Value.Start, fnNode.NameSpan.Value.Length));
        SyntaxNode constNode = tree.Root.Children[1];
        Assert.Equal("Point", src.Substring(constNode.NameSpan!.Value.Start, constNode.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_ConstVar_TopLevel()
    {
        string src = """
const MAX: usize = 100;

var counter: i32 = 0;

pub const name = "test";
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "const" && o.Label == "MAX");
        Assert.Contains(outline, static o => o.Kind == "var" && o.Label == "counter");
        Assert.Contains(outline, static o => o.Kind == "const" && o.Label == "name");
    }

    [Fact]
    public void Parse_EnumUnion_Types()
    {
        string src = """
pub const Color = enum { Red, Green, Blue };

pub const Data = union { i: i32, f: f64 };
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "enum" && o.Label == "Color");
        Assert.Contains(outline, static o => o.Kind == "union" && o.Label == "Data");
    }

    [Fact]
    public void Parse_TestBlocks()
    {
        string src = """
test "basic" {
    try expect(true);
}

test {
    try expect(false);
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);
        Assert.All(outline, static o => Assert.Equal("test", o.Kind));
    }

    [Fact]
    public void Parse_StringSafety_KeywordsInStringsNoStructure()
    {
        string src = """
pub fn real() void {}

const s = "fn fake() {}";
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        IReadOnlyList<OutlineNode> fns = outline.Where(static o => o.Kind == "fn").ToList();
        Assert.Single(fns);
        Assert.Equal("real", fns[0].Label);
    }

    [Fact]
    public void Parse_CommentSafety_KeywordsInCommentsNoStructure()
    {
        string src = """
// fn fake() {}
/// doc fn also_fake() {}
pub fn real() void {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        IReadOnlyList<OutlineNode> fns = outline.Where(static o => o.Kind == "fn").ToList();
        Assert.Single(fns);
        Assert.Equal("real", fns[0].Label);
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("fn f( {\n"));
        Assert.NotNull(tree.Root);
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "ZIG001");
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "pub fn helper() void {}\n" + new string(' ', 100) + "\n";
        ParseOptions options = new() { MaxChars = 20 };
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
            SourceText.From("pub fn helper() void {}"),
            cancellationToken: cts.Token));
    }

    [Fact]
    public void Parse_NoThrowFuzz_TruncatedInputsNeverThrow()
    {
        string src = """
pub const App = struct {
    fn run(self: *App) void {}
};

pub fn main() void {}
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
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From("pub fn helper() void {}\n"));
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
            Task.Run(() => a = _pack.Parse(SourceText.From("pub fn alpha() void {}\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("pub fn beta() void {}\n"))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "beta");
    }
}
