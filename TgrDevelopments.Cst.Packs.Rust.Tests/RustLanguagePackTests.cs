using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Rust;

namespace TgrDevelopments.Cst.Packs.Rust.Tests;

public class RustLanguagePackTests
{
    private readonly RustLanguagePack _pack = new();

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
    public void Parse_Happy_WorkedExample_ModStructImplFn()
    {
        string src = """
mod util {
  pub fn helper() {}
}

pub struct App;

impl App {
  pub fn run(&self) {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(3, outline.Count);

        OutlineNode modNode = outline[0];
        Assert.Equal("mod", modNode.Kind);
        Assert.Equal("util", modNode.Label);

        OutlineNode structNode = outline[1];
        Assert.Equal("struct", structNode.Kind);
        Assert.Equal("App", structNode.Label);
        Assert.Equal(5, structNode.LineSpan.Start.Line);

        OutlineNode implNode = outline[2];
        Assert.Equal("impl", implNode.Kind);
        Assert.NotNull(implNode.Children);
        OutlineNode methodNode = Assert.Single(implNode.Children);
        Assert.Equal("method", methodNode.Kind);
        Assert.Equal("run", methodNode.Label);
        Assert.Equal(2, methodNode.Level);
    }

    [Fact]
    public void Parse_Nested_ModContainsFn_ImplContainsMethod()
    {
        string src = """
mod outer {
  fn alpha() {}

  mod inner {
    fn beta() {}
  }
}

impl Foo {
  fn bar(&self) {}
  fn baz(&self) {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);

        OutlineNode outer = outline[0];
        Assert.Equal("mod", outer.Kind);
        Assert.Equal("outer", outer.Label);
        Assert.Equal(1, outer.Level);
        Assert.NotNull(outer.Children);
        Assert.Equal(2, outer.Children.Count);

        OutlineNode alpha = outer.Children[0];
        Assert.Equal("fn", alpha.Kind);
        Assert.Equal("alpha", alpha.Label);
        Assert.Equal(2, alpha.Level);
        Assert.True(alpha.LineSpan.Start.Line >= outer.LineSpan.Start.Line);
        Assert.True(alpha.LineSpan.End.Line <= outer.LineSpan.End.Line);

        OutlineNode inner = outer.Children[1];
        Assert.Equal("mod", inner.Kind);
        Assert.Equal("inner", inner.Label);
        Assert.NotNull(inner.Children);
        OutlineNode beta = inner.Children[0];
        Assert.Equal("fn", beta.Kind);
        Assert.Equal("beta", beta.Label);

        OutlineNode implNode = outline[1];
        Assert.Equal("impl", implNode.Kind);
        Assert.NotNull(implNode.Children);
        Assert.Equal(2, implNode.Children.Count);
        Assert.Equal("method", implNode.Children[0].Kind);
        Assert.Equal("bar", implNode.Children[0].Label);
        Assert.Equal("method", implNode.Children[1].Kind);
        Assert.Equal("baz", implNode.Children[1].Label);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = """
fn add(x: i32, y: i32) -> i32 { x + y }

struct Point { x: i32, y: i32 }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode fnNode = tree.Root.Children[0];
        Assert.Equal("add", src.Substring(fnNode.NameSpan!.Value.Start, fnNode.NameSpan.Value.Length));
        SyntaxNode structNode = tree.Root.Children[1];
        Assert.Equal("Point", src.Substring(structNode.NameSpan!.Value.Start, structNode.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_Generics_FnTypeImpl()
    {
        string src = """
fn identity<T>(x: T) -> T { x }

struct Pair<T, U> { first: T, second: U }

impl<T> Pair<T, T> {
  fn swap(self) -> Pair<T, T> { self }
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "fn" && o.Label == "identity");
        Assert.Contains(outline, static o => o.Kind == "struct" && o.Label == "Pair");
        OutlineNode implNode = outline.First(static o => o.Kind == "impl");
        Assert.Contains(implNode.Children, static c => c.Kind == "method" && c.Label == "swap");
    }

    [Fact]
    public void Parse_Attributes_ConsumedBeforeItem()
    {
        string src = """
#[derive(Debug)]
struct Point { x: i32, y: i32 }

#[cfg(test)]
mod tests {
  fn check() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "struct" && o.Label == "Point");
        Assert.Contains(outline, static o => o.Kind == "mod" && o.Label == "tests");
    }

    [Fact]
    public void Parse_StringSafety_KeywordsInStringsNoStructure()
    {
        string src = """
fn real() {}

let _ = "fn fake() {}";
let _ = r#"impl Fake { fn method() {} }"#;
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("fn", outline[0].Kind);
        Assert.Equal("real", outline[0].Label);
    }

    [Fact]
    public void Parse_CommentSafety_KeywordsInCommentsNoStructure()
    {
        string src = """
// fn hidden() {}
/* fn also_hidden() {} */
fn real() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("real", outline[0].Label);
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("fn f( {\n"));
        Assert.NotNull(tree.Root);
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "RS001");
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "fn helper() {}\n" + new string(' ', 100) + "\n";
        SourceText source = SourceText.From(text);
        ParseOptions options = new() { MaxChars = 15 };

        ConcreteSyntaxTree tree = _pack.Parse(source, options);

        Assert.Contains(tree.Diagnostics, static d => d.Code == "CST_LIMIT");
        Assert.True(tree.IsPartial);
        Assert.True(tree.Source.Text.Length <= 15);
    }

    [Fact]
    public void Parse_CancelledToken_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => _pack.Parse(
            SourceText.From("fn helper() {}"),
            cancellationToken: cts.Token));
    }

    [Fact]
    public void Parse_NoThrowFuzz_TruncatedInputsNeverThrow()
    {
        string src = """
mod util {
  fn helper() {}
}

impl App {
  fn run(&self) {}
  fn stop(&self) {}
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
    public void Parse_ConstStatic_TopLevelOutlines()
    {
        string src = """
const MAX: usize = 100;

static NAME: &str = "app";
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "const" && o.Label == "MAX");
        Assert.Contains(outline, static o => o.Kind == "static" && o.Label == "NAME");
    }

    [Fact]
    public void Parse_ConstFn_OutlinedAsFunction()
    {
        string src = """
const fn add(x: i32, y: i32) -> i32 { x + y }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "fn" && o.Label == "add");
    }

    [Fact]
    public void Parse_StackedPrefixes_IncludingConst()
    {
        string src = """
pub unsafe extern "C" fn c_api() -> i32 { 42 }
pub const fn compile_time(x: i32) -> i32 { x }
const fn simple() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Label == "c_api");
        Assert.Contains(outline, static o => o.Label == "compile_time");
        Assert.Contains(outline, static o => o.Label == "simple");
    }

    [Fact]
    public void Parse_Trait_HasChildren()
    {
        string src = """
trait Draw {
  fn draw(&self);
  fn resize(&self, w: i32, h: i32);
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode trait = Assert.Single(outline);
        Assert.Equal("trait", trait.Kind);
        Assert.Equal("Draw", trait.Label);
        Assert.NotNull(trait.Children);
        Assert.Equal(2, trait.Children.Count);
        Assert.All(trait.Children, static c => Assert.Equal("method", c.Kind));
    }

    [Fact]
    public void GetOutline_SharedInstance_IndependentOfLaterParse()
    {
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From("fn helper() {}\n"));
        ConcreteSyntaxTree empty = _pack.Parse(SourceText.From(""));
        Assert.NotEmpty(_pack.GetOutline(happy));
        Assert.Empty(_pack.GetOutline(empty));
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("fn alpha() {}\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("fn beta() {}\n"))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        IReadOnlyList<OutlineNode> oA = _pack.GetOutline(a!);
        IReadOnlyList<OutlineNode> oB = _pack.GetOutline(b!);
        Assert.Contains(oA, static o => o.Label == "alpha");
        Assert.Contains(oB, static o => o.Label == "beta");
        Assert.DoesNotContain(oA, static o => o.Label == "beta");
    }
}
