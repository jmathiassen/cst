using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Php;

namespace TgrDevelopments.Cst.Packs.Php.Tests;

public class PhpLanguagePackTests
{
    private readonly PhpLanguagePack _pack = new();

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
<?php
class Engine {
    public function run() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode cls = Assert.Single(outline);
        Assert.Equal("class", cls.Kind);
        Assert.Equal("Engine", cls.Label);
        Assert.Equal(2, cls.LineSpan.Start.Line);
        Assert.Equal(4, cls.LineSpan.End.Line);
        Assert.NotNull(cls.Children);
        OutlineNode method = Assert.Single(cls.Children);
        Assert.Equal("method", method.Kind);
        Assert.Equal("run", method.Label);
        Assert.Equal(3, method.LineSpan.Start.Line);
        Assert.Equal(3, method.LineSpan.End.Line);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = """
<?php
class Main {
    function run() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode cls = tree.Root.Children[0];
        Assert.Equal("Main", src.Substring(cls.NameSpan!.Value.Start, cls.NameSpan.Value.Length));
        SyntaxNode method = cls.Children[0];
        Assert.Equal("run", src.Substring(method.NameSpan!.Value.Start, method.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_TraitInterfaceEnum_Types()
    {
        string src = """
<?php
trait Loggable {
    function log() {}
}

interface Drawable {
    function draw();
}

enum Status {
    case Active;
    case Inactive;
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "trait" && o.Label == "Loggable");
        Assert.Contains(outline, static o => o.Kind == "interface" && o.Label == "Drawable");
        Assert.Contains(outline, static o => o.Kind == "enum" && o.Label == "Status");
    }

    [Fact]
    public void Parse_TopLevelFunction()
    {
        string src = """
<?php
function helper() {}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("function", outline[0].Kind);
        Assert.Equal("helper", outline[0].Label);
    }

    [Fact]
    public void Parse_Heredoc_NoStructure()
    {
        string src = "<?php\nfunction real() {\n    $s = <<<EOT\nclass Fake { }\nEOT;\n}\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("real", outline[0].Label);
    }

    [Fact]
    public void Parse_Namespace_TopLevelAndChildren()
    {
        string src = """
<?php
namespace App\Engine {
    function helper() {}
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.NotEmpty(outline);
    }

    [Fact]
    public void Parse_StringSafety_NoStructure()
    {
        string src = "<?php\nfunction real() {\n    $s = \"class Fake { function m() {} }\";\n}\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("real", outline[0].Label);
    }

    [Fact]
    public void Parse_CommentSafety_NoStructure()
    {
        string src = "<?php\nfunction real() {\n    // class Fake {}\n    /* class AlsoFake {} */\n}\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("real", outline[0].Label);
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("<?php\nclass X {\n"));
        Assert.NotNull(tree.Root);
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "PHP001");
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "<?php\nclass A {}\n" + new string(' ', 100) + "\n";
        ParseOptions options = new() { MaxChars = 15 };
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
            SourceText.From("<?php\nclass A {}"),
            cancellationToken: cts.Token));
    }

    [Fact]
    public void Parse_NoThrowFuzz_TruncatedInputsNeverThrow()
    {
        string src = "<?php\nclass Engine {\n    function run() {}\n}\n";
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
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From("<?php\nclass A { function m() {} }\n"));
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
            Task.Run(() => a = _pack.Parse(SourceText.From("<?php\nclass Alpha { function a() {} }\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("<?php\nclass Beta { function b() {} }\n"))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }
}
