using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Ruby;

namespace TgrDevelopments.Cst.Packs.Ruby.Tests;

public class RubyLanguagePackTests
{
    private readonly RubyLanguagePack _pack = new();

    [Fact]
    public void Parse_Empty_OutlineEmpty()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        Assert.NotNull(tree.Root);
        Assert.Equal(SyntaxQuality.Structural, tree.Quality);
        Assert.Empty(_pack.GetOutline(tree));
    }

    [Fact]
    public void Parse_Happy_ModuleClassDef()
    {
        string src = """
module App
  class Engine
    def run
    end
  end
end
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode mod = Assert.Single(outline);
        Assert.Equal("module", mod.Kind);
        Assert.Equal("App", mod.Label);
        Assert.Equal(1, mod.LineSpan.Start.Line);
        Assert.Equal(5, mod.LineSpan.End.Line);
        Assert.NotNull(mod.Children);
        OutlineNode cls = Assert.Single(mod.Children);
        Assert.Equal("class", cls.Kind);
        Assert.Equal("Engine", cls.Label);
        Assert.Equal(2, cls.LineSpan.Start.Line);
        Assert.Equal(4, cls.LineSpan.End.Line);
        Assert.NotNull(cls.Children);
        OutlineNode defNode = Assert.Single(cls.Children);
        Assert.Equal("method", defNode.Kind);
        Assert.Equal("run", defNode.Label);
        Assert.Equal(3, defNode.LineSpan.Start.Line);
        Assert.Equal(3, defNode.LineSpan.End.Line);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = """
def greet(name)
  puts name
end
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode fn = tree.Root.Children[0];
        Assert.Equal("greet", src.Substring(fn.NameSpan!.Value.Start, fn.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_ClassWithSuperclass()
    {
        string src = """
class Vehicle < ActiveRecord::Base
  def move
  end
end
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Equal("class", cls.Kind);
        Assert.Equal("Vehicle", cls.Label);
    }

    [Fact]
    public void Parse_DefWithOperatorsAndPredicates()
    {
        string src = """
class Foo
  def ==(other)
    true
  end
  def valid?
    true
  end
  def apply!
    true
  end
end
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Equal(3, cls.Children!.Count);
        Assert.Contains(cls.Children, static c => c.Label == "==");
        Assert.Contains(cls.Children, static c => c.Label == "valid?");
        Assert.Contains(cls.Children, static c => c.Label == "apply!");
    }

    [Fact]
    public void Parse_DefSelf_ClassMethod()
    {
        string src = """
class Factory
    def self.make
    end
    def instance
    end
end
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Equal("Factory", cls.Label);
        Assert.Equal(2, cls.Children!.Count);
        Assert.Contains(cls.Children, static c => c.Label == "make");
        Assert.Contains(cls.Children, static c => c.Label == "instance");
    }

    [Fact]
    public void Parse_NestedDef_InsideMethod()
    {
        string src = """
def outer
    def inner
    end
end
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode outerFn = Assert.Single(outline);
        Assert.Equal("outer", outerFn.Label);
        Assert.NotNull(outerFn.Children);
        Assert.Single(outerFn.Children);
        Assert.Equal("inner", outerFn.Children[0].Label);
    }

    [Fact]
    public void Parse_StringSafety_NoStructure()
    {
        string src = "class Real\n\ndef real\n  s = \"class Fake\\ndef fake\\nend\"\nend\nend\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Equal("Real", cls.Label);
        Assert.Single(cls.Children!);
        Assert.Equal("real", cls.Children![0].Label);
    }

    [Fact]
    public void Parse_CommentSafety_NoStructure()
    {
        string src = "class Real\n  # class Fake\n  def real\n  end\nend\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Single(cls.Children!);
        Assert.Equal("real", cls.Children![0].Label);
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("def f(\n"));
        Assert.NotNull(tree.Root);
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "RUBY001");
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "class A\nend\n" + new string(' ', 100) + "\n";
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
            SourceText.From("class A\nend\n"),
            cancellationToken: cts.Token));
    }

    [Fact]
    public void Parse_NoThrowFuzz_TruncatedInputsNeverThrow()
    {
        string src = "class Engine\n  def run\n  end\nend\n";
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
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From("class A\n  def m\n  end\nend\n"));
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
            Task.Run(() => a = _pack.Parse(SourceText.From("class Alpha\n  def a\n  end\nend\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("class Beta\n  def b\n  end\nend\n"))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }
}
