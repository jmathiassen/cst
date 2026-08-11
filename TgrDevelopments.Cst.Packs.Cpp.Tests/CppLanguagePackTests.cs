using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Cpp;

namespace TgrDevelopments.Cst.Packs.Cpp.Tests;

public class CppLanguagePackTests
{
    private readonly CppLanguagePack _pack = new();

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
    public void Parse_Happy_WorkedExample_NamespaceClassMethod()
    {
        string src = """
namespace app {
  class Engine {
  public:
    void run();
  };
  void Engine::run() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);

        OutlineNode ns = outline[0];
        Assert.Equal("namespace", ns.Kind);
        Assert.Equal("app", ns.Label);
        Assert.Equal(1, ns.Level);
        Assert.Equal(1, ns.LineSpan.Start.Line);
        Assert.Equal(7, ns.LineSpan.End.Line);
        Assert.Equal(2, ns.Children.Count);

        OutlineNode cls = ns.Children[0];
        Assert.Equal("class", cls.Kind);
        Assert.Equal("Engine", cls.Label);
        Assert.Equal(2, cls.Level);
        Assert.Equal(2, cls.LineSpan.Start.Line);
        Assert.Equal(5, cls.LineSpan.End.Line);

        OutlineNode method = Assert.Single(cls.Children);
        Assert.Equal("method", method.Kind);
        Assert.Equal("run", method.Label);
        Assert.Equal(3, method.Level);
        Assert.Equal(4, method.LineSpan.Start.Line);

        OutlineNode outOfLine = ns.Children[1];
        Assert.Equal("method", outOfLine.Kind);
        Assert.Equal("run", outOfLine.Label);
        Assert.Equal(2, outOfLine.Level);
        Assert.Equal(6, outOfLine.LineSpan.Start.Line);
        Assert.Equal(6, outOfLine.LineSpan.End.Line);
    }

    [Fact]
    public void Parse_Happy_NameSpanPointsAtIdentifier()
    {
        string src = """
namespace app {
  class Engine {
    void run() {}
  };
}
template <class T>
T max_value(T a, T b) { return a; }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode ns = tree.Root.Children[0];
        Assert.Equal("app", src.Substring(ns.NameSpan!.Value.Start, ns.NameSpan.Value.Length));
        SyntaxNode cls = ns.Children[0];
        Assert.Equal("Engine", src.Substring(cls.NameSpan!.Value.Start, cls.NameSpan.Value.Length));
        SyntaxNode method = cls.Children[0];
        Assert.Equal("run", src.Substring(method.NameSpan!.Value.Start, method.NameSpan.Value.Length));
        SyntaxNode fn = tree.Root.Children[1];
        Assert.Equal("max_value", src.Substring(fn.NameSpan!.Value.Start, fn.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_Templates_ClassAndFunctionNames()
    {
        string src = """
template <typename T, int N>
class Buffer {
public:
  void push(const T& value);
};
template <class T>
std::vector<T> make_vector(T first) { return {}; }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "class" && o.Label == "Buffer");
        Assert.Contains(outline, static o => o.Kind == "function" && o.Label == "make_vector");
    }

    [Fact]
    public void Parse_Operators_OperatorOverloadName()
    {
        string src = """
class Foo {
public:
  bool operator==(const Foo& other) const;
  Foo& operator=(const Foo& other);
  int operator()(int x);
};
std::ostream& operator<<(std::ostream& os, const Foo& f) { return os; }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = outline.First(o => o.Kind == "class");
        Assert.Contains(cls.Children, static o => o.Label == "operator==" && o.Kind == "method");
        Assert.Contains(cls.Children, static o => o.Label == "operator=" && o.Kind == "method");
        Assert.Contains(cls.Children, static o => o.Label == "operator()" && o.Kind == "method");
        Assert.Contains(outline, static o => o.Label == "operator<<" && o.Kind == "function");
    }

    [Fact]
    public void Parse_CtorDtor_KindsAndLabels()
    {
        string src = """
class Engine {
public:
  Engine(int hp);
  ~Engine();
  Engine(const Engine& other) {}
};
Engine::~Engine() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = outline.First(o => o.Kind == "class");
        Assert.Contains(cls.Children, static o => o.Kind == "constructor" && o.Label == "Engine");
        Assert.Contains(cls.Children, static o => o.Kind == "destructor" && o.Label == "~Engine");
        Assert.Contains(outline, static o => o.Kind == "destructor" && o.Label == "~Engine");
    }

    [Fact]
    public void Parse_Preprocessor_InactiveRegionNoStructure()
    {
        string src = """
#if 0
class Dead {
public:
  void never();
};
#endif
class Alive {
public:
  void live();
};
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("Alive", outline[0].Label);
        Assert.Contains(outline[0].Children, static o => o.Label == "live");
    }

    [Fact]
    public void Parse_MacroCall_NoFalseFunctionOutline()
    {
        string src = """
#define FOO(x) class x {};
FOO(Fake);
class Real {};
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label == "FOO");
        Assert.DoesNotContain(outline, static o => o.Label == "Fake");
        Assert.Contains(outline, static o => o.Label == "Real");
    }

    [Fact]
    public void Parse_StringSafety_KeywordsInStringsNoStructure()
    {
        string src = """
const char* text = "class Engine { void run(); } namespace app";
const char* raw = R"(struct Fake { int x; })";
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        Assert.Empty(_pack.GetOutline(tree));
    }

    [Fact]
    public void Parse_CommentSafety_KeywordsInCommentsNoStructure()
    {
        string src = """
// class Hidden { void never(); };
/* struct AlsoHidden { }; */
/// namespace docs { }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        Assert.Empty(_pack.GetOutline(tree));
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("class X {\n"));
        Assert.NotNull(tree.Root);
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "CPP001");
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "namespace app {\n" + new string(' ', 100) + "\n}\n";
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
namespace app {
  template <class T>
  class Buffer {
  public:
    void push(const T& value) {}
    bool operator==(const Buffer& other) const { return true; }
  };
  void Engine::run() {}
  static_assert(sizeof(int) == 4, "size");
  const char* raw = R"delim(abc)delim";
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
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From("namespace app {\n  class Engine {\n  public:\n    void run();\n  };\n}\n"));
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
            Task.Run(() => a = _pack.Parse(SourceText.From("namespace app {\n  class Engine {\n  public:\n    void run();\n  };\n}\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From(""))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        IReadOnlyList<OutlineNode> oA = _pack.GetOutline(a!);
        IReadOnlyList<OutlineNode> oB = _pack.GetOutline(b!);
        Assert.NotEmpty(oA);
        Assert.Empty(oB);
    }

    [Fact]
    public void Parse_CancelledToken_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => _pack.Parse(
            SourceText.From("namespace app {\n  class Engine {\n  public:\n    void run();\n  };\n}\n"),
            cancellationToken: cts.Token));
    }
}
