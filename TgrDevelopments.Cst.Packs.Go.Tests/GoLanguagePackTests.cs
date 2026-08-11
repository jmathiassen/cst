using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Go;

namespace TgrDevelopments.Cst.Packs.Go.Tests;

public class GoLanguagePackTests
{
    private readonly GoLanguagePack _pack = new();

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
    public void Parse_Happy_WorkedExample_PackageTypeMethodFunc()
    {
        string src = """
package app

type Engine struct{}

func (e Engine) Run() {}

func Helper() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(3, outline.Count);

        Assert.Equal("package", outline[0].Kind);
        Assert.Equal("app", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);

        Assert.Equal("type", outline[1].Kind);
        Assert.Equal("Engine", outline[1].Label);
        Assert.Equal(1, outline[1].Level);
        Assert.Equal(3, outline[1].LineSpan.Start.Line);

        OutlineNode method = Assert.Single(outline[1].Children);
        Assert.Equal("method", method.Kind);
        Assert.Equal("Run", method.Label);
        Assert.Equal(2, method.Level);
        Assert.Equal(5, method.LineSpan.Start.Line);

        Assert.Equal("func", outline[2].Kind);
        Assert.Equal("Helper", outline[2].Label);
        Assert.Equal(1, outline[2].Level);
        Assert.Equal(7, outline[2].LineSpan.Start.Line);
    }

    [Fact]
    public void Parse_Happy_NameSpanPointsAtIdentifier()
    {
        string src = """
package app

type Engine struct{}

func (e Engine) Run() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode pkg = tree.Root.Children[0];
        Assert.Equal("app", src.Substring(pkg.NameSpan!.Value.Start, pkg.NameSpan.Value.Length));
        SyntaxNode typeNode = tree.Root.Children[1];
        Assert.Equal("Engine", src.Substring(typeNode.NameSpan!.Value.Start, typeNode.NameSpan.Value.Length));
        Assert.Contains(typeNode.Children, static c => c.Kind == "struct_type");
        SyntaxNode method = Assert.Single(typeNode.Children, static c => c.Kind == "method_declaration");
        Assert.Equal("Run", src.Substring(method.NameSpan!.Value.Start, method.NameSpan.Value.Length));
        Assert.Equal("Engine", method.Properties!["receiver"]);
    }

    [Fact]
    public void Parse_Generics_TypeParamsSkipped()
    {
        string src = """
package app

type Stack[T any] struct {
  items []T
}

func (s *Stack[T]) Push(v T) {}

func Map[K comparable, V any](fn func(K) V, m map[K]V) []V { return nil }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode stack = Assert.Single(outline, static o => o.Kind == "type" && o.Label == "Stack");
        OutlineNode push = Assert.Single(stack.Children, static o => o.Kind == "method" && o.Label == "Push");
        Assert.Equal(2, push.Level);
        Assert.Contains(outline, static o => o.Kind == "func" && o.Label == "Map");
    }

    [Fact]
    public void Parse_VarConst_SingleAndBlocks()
    {
        string src = """
package app

var x = 1

const (
  Pi = 3.14
  Max = 100
)

var (
  a, b int
  c = []int{1, 2}
)
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "var" && o.Label == "x");
        Assert.Contains(outline, static o => o.Kind == "const" && o.Label == "Pi");
        Assert.Contains(outline, static o => o.Kind == "const" && o.Label == "Max");
        Assert.Contains(outline, static o => o.Kind == "var" && o.Label == "a, b");
        Assert.Contains(outline, static o => o.Kind == "var" && o.Label == "c");
    }

    [Fact]
    public void Parse_StructInterface_UnderlyingTypeAndBodyNodes()
    {
        string src = """
package app

type Handler interface {
  Handle(ctx Context) error
}

type State struct {
  value int
}

type Alias = map[string]State
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(4, outline.Count);
        Assert.Contains(outline, static o => o.Label == "Handler" && o.Kind == "type");
        Assert.Contains(outline, static o => o.Label == "State" && o.Kind == "type");
        Assert.Contains(outline, static o => o.Label == "Alias" && o.Kind == "type");

        SyntaxNode state = tree.Root.Children.Single(c => c.Name == "State");
        SyntaxNode body = Assert.Single(state.Children);
        Assert.Equal("struct_type", body.Kind);
        SyntaxNode handler = tree.Root.Children.Single(c => c.Name == "Handler");
        Assert.Equal("interface_type", Assert.Single(handler.Children).Kind);
    }

    [Fact]
    public void Parse_StringSafety_KeywordsInStringsNoStructure()
    {
        string src = """
package app

const a = "func Fake() {}"
const b = `type AlsoFake struct {
  method() int
}`
const c = 'f'

func Real() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label == "Fake");
        Assert.DoesNotContain(outline, static o => o.Label == "AlsoFake");
        Assert.Contains(outline, static o => o.Label == "Real");
    }

    [Fact]
    public void Parse_RawStringInFunctionBody_OutlinedCorrectly()
    {
        string src = """
package app

func process(data []byte) {
    s := `some { data } here`
    _ = s
}

func helper() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Label == "process");
        Assert.Contains(outline, static o => o.Label == "helper");
    }

    [Fact]
    public void Parse_RawStringWithParensInFunctionBody_OutlinedCorrectly()
    {
        string src = """
package app

func format(s string) string {
    return fmt.Sprintf(`hello(world): %s`, s)
}

func main() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Label == "format");
        Assert.Contains(outline, static o => o.Label == "main");
    }

    [Fact]
    public void Parse_RawStringAsLastBodyStatement_LastInBody_OutlinedCorrectly()
    {
        string src = """
package app

func f() {
    s := `hello ( world ) { brace }`
}

func peer() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Label == "f");
        Assert.Contains(outline, static o => o.Label == "peer");
    }

    [Fact]
    public void Parse_RawStringWithFunctionLiteral_LastInBody_OutlinedCorrectly()
    {
        string src = """
package main

func real() {
    s := `func fake() {}`
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Label == "real");
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
    }

    [Fact]
    public void Parse_PredeclaredIdentifiersAsNames_OutlinedCorrectly()
    {
        string src = """
package main

func make() {}

func len(x int) int { return x }

var true = 1

const false = 2

type any = int

var iota = 0
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Label == "make");
        Assert.Contains(outline, static o => o.Label == "len");
        Assert.Contains(outline, static o => o.Label == "true");
        Assert.Contains(outline, static o => o.Label == "false");
        Assert.Contains(outline, static o => o.Label == "any");
        Assert.Contains(outline, static o => o.Label == "iota");
    }

    [Fact]
    public void Parse_Methods_MultipleMethodsNestedUnderReceiverType()
    {
        string src = """
package app

type Engine struct{}

func (e Engine) Run() {}

func (e Engine) Stop() {}

func Free() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode engine = Assert.Single(outline, static o => o.Kind == "type" && o.Label == "Engine");
        Assert.Equal(2, engine.Children.Count);
        Assert.Contains(engine.Children, static o => o.Kind == "method" && o.Label == "Run");
        Assert.Contains(engine.Children, static o => o.Kind == "method" && o.Label == "Stop");
        Assert.Contains(outline, static o => o.Kind == "func" && o.Label == "Free");
    }

    [Fact]
    public void Parse_Methods_MethodDeclaredBeforeType_NestedWhenTypeFound()
    {
        string src = """
package app

func (e *Engine) Run() {}

type Engine struct{}

func (e Engine) Stop() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode engine = Assert.Single(outline, static o => o.Kind == "type" && o.Label == "Engine");
        Assert.Equal(2, engine.Children.Count);
        Assert.Contains(engine.Children, static o => o.Kind == "method" && o.Label == "Run");
        Assert.Contains(engine.Children, static o => o.Kind == "method" && o.Label == "Stop");
    }

    [Fact]
    public void Parse_MalformedFunctionDeclaration_ResyncsToNextDeclaration()
    {
        string src = """
package app

func ( {}

func Real() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "GO001");
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "func" && o.Label == "Real");
    }

    [Fact]
    public void Parse_MalformedTypeDeclaration_ResyncsToNextDeclaration()
    {
        string src = """
package app

type ( {}

func Real() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "GO001");
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "func" && o.Label == "Real");
    }

    [Fact]
    public void Parse_CommentSafety_KeywordsInCommentsNoStructure()
    {
        string src = """
package app

// func Hidden() {}
/* type AlsoHidden struct { } */
func Real() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label == "Hidden");
        Assert.DoesNotContain(outline, static o => o.Label == "AlsoHidden");
        Assert.Contains(outline, static o => o.Label == "Real");
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("func F( {\n"));
        Assert.NotNull(tree.Root);
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "GO001");
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "package app\n" + new string(' ', 100) + "\n";
        SourceText source = SourceText.From(text);
        ParseOptions options = new() { MaxChars = 10 };

        ConcreteSyntaxTree tree = _pack.Parse(source, options);

        Assert.Contains(tree.Diagnostics, static d => d.Code == "CST_LIMIT");
        Assert.True(tree.IsPartial);
        Assert.True(tree.Source.Text.Length <= 10);
    }

    [Fact]
    public void Parse_CancelledToken_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => _pack.Parse(
            SourceText.From("package app\nfunc Real() {}\n"),
            cancellationToken: cts.Token));
    }

    [Fact]
    public void Parse_NoThrowFuzz_TruncatedInputsNeverThrow()
    {
        string src = """
package app

type Engine struct {
  hp int
}

func (e *Engine) Run() error {
  if e.hp > 0 {
    return nil
  }
  return fmt.Errorf("dead")
}

const (
  Pi = 3.14
)

var pool = []int{1, 2, 3}
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
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From("package app\nfunc Helper() {}\n"));
        ConcreteSyntaxTree empty = _pack.Parse(SourceText.From(""));
        IReadOnlyList<OutlineNode> emptyOutline = _pack.GetOutline(empty);
        IReadOnlyList<OutlineNode> happyOutline = _pack.GetOutline(happy);
        Assert.Empty(emptyOutline);
        Assert.NotEmpty(happyOutline);
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_NoThrowDistinctTrees()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("package a\nfunc Alpha() {}\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("package b\nfunc Beta() {}\n"))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        IReadOnlyList<OutlineNode> oA = _pack.GetOutline(a!);
        IReadOnlyList<OutlineNode> oB = _pack.GetOutline(b!);
        Assert.Contains(oA, static o => o.Label is "a" or "Alpha");
        Assert.Contains(oB, static o => o.Label is "b" or "Beta");
        Assert.DoesNotContain(oA, static o => o.Label is "b" or "Beta");
    }
}
