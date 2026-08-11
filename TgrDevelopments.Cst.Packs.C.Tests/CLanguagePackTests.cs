using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.C;

namespace TgrDevelopments.Cst.Packs.C.Tests;

public class CLanguagePackTests
{
    private readonly CLanguagePack _pack = new();

    [Fact]
    public void Parse_Empty_OutlineEmpty()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        Assert.Equal(SyntaxConfidence.Syntax, tree.Confidence);
        Assert.Equal(SyntaxQuality.Structural, tree.Quality);
        Assert.Empty(_pack.GetOutline(tree));
        Assert.NotNull(tree.CachedOutline);
        Assert.Empty(tree.CachedOutline!);
    }

    [Fact]
    public void Parse_Happy_FunctionStructTypedefLabelsAndLines()
    {
        string src = """
struct Point {
  int x;
};
typedef struct Point Point;
int add(int a, int b) {
  return a + b;
}
enum Color { RED, GREEN };
typedef enum { A, B } Mode;
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Same(tree.CachedOutline, outline);
        Assert.Equal(5, outline.Count);
        Assert.Equal("struct", outline[0].Kind);
        Assert.Equal("Point", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);
        Assert.Equal(3, outline[0].LineSpan.End.Line);
        Assert.Equal("typedef", outline[1].Kind);
        Assert.Equal("Point", outline[1].Label);
        Assert.Equal(4, outline[1].LineSpan.Start.Line);
        Assert.Equal(4, outline[1].LineSpan.End.Line);
        Assert.Equal("function", outline[2].Kind);
        Assert.Equal("add", outline[2].Label);
        Assert.Equal(5, outline[2].LineSpan.Start.Line);
        Assert.Equal(7, outline[2].LineSpan.End.Line);
        Assert.Equal("enum", outline[3].Kind);
        Assert.Equal("Color", outline[3].Label);
        Assert.Equal(8, outline[3].LineSpan.Start.Line);
        Assert.Equal(8, outline[3].LineSpan.End.Line);
        Assert.Equal("typedef", outline[4].Kind);
        Assert.Equal("Mode", outline[4].Label);
        Assert.Equal(9, outline[4].LineSpan.Start.Line);
        Assert.Equal(9, outline[4].LineSpan.End.Line);
    }

    [Fact]
    public void Parse_KnrStyleDefinition_OutlinedAsSingleFunction()
    {
        string src = """
int add(a, b)
int a;
int b;
{
  return a + b;
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Single(outline);
        Assert.Equal("function", outline[0].Kind);
        Assert.Equal("add", outline[0].Label);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);
        Assert.Equal(6, outline[0].LineSpan.End.Line);
    }

    [Fact]
    public void Parse_KnrStyleDefinition_WithPointerParameter_Outlined()
    {
        string src = """
int copy(dest, src)
char *dest;
const char *src;
{
  return 0;
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Single(outline);
        Assert.Equal("function", outline[0].Kind);
        Assert.Equal("copy", outline[0].Label);
    }

    [Fact]
    public void Parse_MultiLineSignatureAndPointerReturn_Outlined()
    {
        string src = """
struct Point {
  int x;
};
struct Point *
make_point(int x) {
  return 0;
}
static inline int
max(int a, int b) {
  return a > b ? a : b;
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Equal(3, outline.Count);
        Assert.Equal("Point", outline[0].Label);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);
        Assert.Equal(3, outline[0].LineSpan.End.Line);
        Assert.Equal("make_point", outline[1].Label);
        Assert.Equal(4, outline[1].LineSpan.Start.Line);
        Assert.Equal(7, outline[1].LineSpan.End.Line);
        Assert.Equal("max", outline[2].Label);
        Assert.Equal(8, outline[2].LineSpan.Start.Line);
        Assert.Equal(11, outline[2].LineSpan.End.Line);
    }

    [Fact]
    public void Parse_DeclarationsAndPrototypes_OnlyFunctionsOutlined()
    {
        string src = """
int counter = 0;
int (*handler)(int);
struct Point;
void notify(void);
char *name;
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Single(outline);
        Assert.Equal("function", outline[0].Kind);
        Assert.Equal("notify", outline[0].Label);
        Assert.Equal(4, outline[0].LineSpan.Start.Line);
        Assert.Equal(4, outline[0].LineSpan.End.Line);
    }

    [Fact]
    public void Parse_StringAndCommentSafety_NoFalseOutlines()
    {
        string src = """
char *s = "int fake(void) {";
/* struct Dead { int x; }; */
char c = '\'';
// int also_fake(void) { }
int real(void) {
  return 1;
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Single(outline);
        Assert.Equal("real", outline[0].Label);
    }

    [Fact]
    public void Parse_FunctionLikeMacro_NoFalseOutline()
    {
        string src = """
#define FOO(x) int x
#define BAR(x, y) int x##y
int real(void) {
  return 1;
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Single(outline);
        Assert.Equal("real", outline[0].Label);
        Assert.Equal(3, outline[0].LineSpan.Start.Line);
        Assert.Equal(5, outline[0].LineSpan.End.Line);
    }

    [Fact]
    public void Parse_FunctionLikeMacroInvocation_DoesNotDropFollowingDecls()
    {
        // Hard case: define body looks like a function decl, then FOO(fake) at
        // file scope, then a real function. Must not outline fake/FOO and must
        // still outline real (no silent empty outline).
        string src = """
#define FOO(x) int x(void) { return 0; }
FOO(fake)
int real(void) {
  return 1;
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label is "fake" or "FOO");
        Assert.Contains(outline, static o => o.Kind == "function" && o.Label == "real");
        OutlineNode real = outline.First(static o => o.Label == "real");
        Assert.Equal(3, real.LineSpan.Start.Line);
        Assert.Equal(5, real.LineSpan.End.Line);
        Assert.False(tree.IsPartial);
    }

    [Fact]
    public void Parse_NakedMacroInvocation_NoFalseFunction_RealSurvives()
    {
        string src = """
#define INIT_DRIVER() do { } while(0)
INIT_DRIVER();
int real(void) {
  return 1;
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "INIT_DRIVER");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_PreprocessorAndInactiveRegions_Skipped()
    {
        string src = """
#include <stdio.h>
#define STRUCT struct
#if 0
int dead(void) {
  return 0;
}
#endif
int alive(void) {
  return 1;
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Single(outline);
        Assert.Equal("alive", outline[0].Label);
        Assert.Equal(8, outline[0].LineSpan.Start.Line);
        Assert.Equal(10, outline[0].LineSpan.End.Line);
    }

    [Fact]
    public void Parse_MultilinePreprocessorDirective_LineContinuationSkipped()
    {
        string src = """
#define LONG_MACRO(x) \
int x(void) { return 0; }
int real(void) {
  return 1;
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Single(outline);
        Assert.Equal("real", outline[0].Label);
    }

    [Fact]
    public void Parse_MultilineIfZero_LineContinuationStillInactive()
    {
        string src = """
#if 0 \
|| 1
int dead(void) { return 0; }
#endif
int alive(void) { return 1; }
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Single(outline);
        Assert.Equal("alive", outline[0].Label);
    }

    [Fact]
    public void Parse_Happy_NameSpanPointsAtIdentifier()
    {
        string src = """
int add(int a, int b) {
  return a + b;
}
struct Point {
  int x;
};
typedef unsigned long ulong_t;
typedef int (*Fn)(int);
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        List<SyntaxNode> nodes = tree.Root.Children.ToList();
        Assert.Equal(4, nodes.Count);
        Assert.All(nodes, static n => Assert.NotNull(n.NameSpan));
        Assert.Equal("add", src.Substring(nodes[0].NameSpan!.Value.Start, nodes[0].NameSpan!.Value.Length));
        Assert.Equal("Point", src.Substring(nodes[1].NameSpan!.Value.Start, nodes[1].NameSpan!.Value.Length));
        Assert.Equal("ulong_t", src.Substring(nodes[2].NameSpan!.Value.Start, nodes[2].NameSpan!.Value.Length));
        Assert.Equal("Fn", src.Substring(nodes[3].NameSpan!.Value.Start, nodes[3].NameSpan!.Value.Length));
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "int big(void) {\n" + new string(' ', 100) + "\n}\n";
        SourceText source = SourceText.From(text);
        ParseOptions options = new() { MaxChars = 10 };

        ConcreteSyntaxTree tree = _pack.Parse(source, options);

        Assert.Contains(tree.Diagnostics, static d => d.Code == "CST_LIMIT");
        Assert.True(tree.IsPartial);
        Assert.True(tree.Source.Text.Length <= 10);
    }

    [Fact]
    public void Parse_NoThrowFuzz_TruncatedInputsNeverThrow()
    {
        string src = """
struct Point {
  int x;
};
typedef int (*Fn)(int);
static inline int max(int a, int b) {
  return a > b ? a : b;
}
#if 0
int dead(void) { return 1; }
#endif
""";
        for (int i = 0; i < src.Length; i++)
        {
            string cut = src[..i];
            ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(cut));
            Assert.NotNull(tree.Root);
        }
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree unclosed = _pack.Parse(SourceText.From("int foo(void) {\n"));
        Assert.NotNull(unclosed.Root);
        Assert.True(unclosed.IsPartial);
        Assert.Contains(unclosed.Diagnostics, static d => d.Code == "C003");

        ConcreteSyntaxTree stray = _pack.Parse(SourceText.From("{ unmatched\n"));
        Assert.NotNull(stray.Root);
        Assert.True(stray.IsPartial);
    }

    [Fact]
    public void GetOutline_SharedInstance_IndependentOfLaterParse()
    {
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From(@"struct Point {
  int x;
};
int add(int a, int b) {
  return a + b;
}
"));
        ConcreteSyntaxTree empty = _pack.Parse(SourceText.From(""));
        Assert.Empty(_pack.GetOutline(empty));
        Assert.Contains(_pack.GetOutline(happy), static o => o.Label == "add");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("int alpha(void) {\n  return 1;\n}\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("int beta(void) {\n  return 2;\n}\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "beta");
    }

    [Fact]
    public void Parse_Cancel_Throws()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            _pack.Parse(SourceText.From("int foo(void) {\n  return 1;\n}\n"), cancellationToken: cts.Token));
    }
}
