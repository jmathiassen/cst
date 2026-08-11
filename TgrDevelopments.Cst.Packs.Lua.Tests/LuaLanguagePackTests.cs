using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Lua;

namespace TgrDevelopments.Cst.Packs.Lua.Tests;

public class LuaLanguagePackTests
{
    private readonly LuaLanguagePack _pack = new();

    [Fact]
    public void Parse_Empty_OutlineEmpty()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        Assert.NotNull(tree.Root);
        Assert.Equal(SyntaxQuality.Structural, tree.Quality);
        Assert.Empty(_pack.GetOutline(tree));
    }

    [Fact]
    public void Parse_Happy_GlobalAndLocalFunction()
    {
        string src = """
local function greet(name)
    print(name)
end

function run()
end
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);
        Assert.Equal("function", outline[0].Kind);
        Assert.Equal("greet", outline[0].Label);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);
        Assert.Equal(3, outline[0].LineSpan.End.Line);
        Assert.Equal("function", outline[1].Kind);
        Assert.Equal("run", outline[1].Label);
        Assert.Equal(5, outline[1].LineSpan.Start.Line);
        Assert.Equal(6, outline[1].LineSpan.End.Line);
    }

    [Fact]
    public void Parse_NameSpan_PointsAtIdentifier()
    {
        string src = """
function add(a, b)
    return a + b
end
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        SyntaxNode fn = tree.Root.Children[0];
        Assert.Equal("add", src.Substring(fn.NameSpan!.Value.Start, fn.NameSpan.Value.Length));
    }

    [Fact]
    public void Parse_DottedAndColonMethods()
    {
        string src = """
function M.run()
end

function M:start()
end
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Contains(outline, static o => o.Kind == "function" && o.Label == "M.run");
        Assert.Contains(outline, static o => o.Kind == "function" && o.Label == "M:start");
    }

    [Fact]
    public void Parse_NestedFunctions()
    {
        string src = """
function outer()
    function inner()
    end
end
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode outer = Assert.Single(outline);
        Assert.Equal("outer", outer.Label);
        Assert.NotNull(outer.Children);
        OutlineNode inner = Assert.Single(outer.Children);
        Assert.Equal("function", inner.Kind);
        Assert.Equal("inner", inner.Label);
        Assert.Equal(2, inner.Level);
    }

    [Fact]
    public void Parse_NestedLocalFunction()
    {
        string src = """
local function outer()
    local function inner()
    end
end
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        OutlineNode outer = Assert.Single(outline);
        Assert.Equal("outer", outer.Label);
        Assert.NotNull(outer.Children);
        OutlineNode inner = Assert.Single(outer.Children);
        Assert.Equal("inner", inner.Label);
        Assert.Equal(2, inner.Level);
    }

    [Fact]
    public void Parse_StringSafety_NoStructure()
    {
        string src = "function real()\n    local s = \"function fake() end\"\nend\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("real", outline[0].Label);
    }

    [Fact]
    public void Parse_LongString_NoStructure()
    {
        string src = "function real()\n    local s = [[function fake() end]]\nend\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("real", outline[0].Label);
    }

    [Fact]
    public void Parse_CommentSafety_NoStructure()
    {
        string src = "function real()\n    --[[ function fake() end ]]\nend\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("real", outline[0].Label);
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("function f(\n"));
        Assert.NotNull(tree.Root);
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "LUA001");
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string text = "function a() end\n" + new string(' ', 100) + "\n";
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
            SourceText.From("function a() end"),
            cancellationToken: cts.Token));
    }

    [Fact]
    public void Parse_NoThrowFuzz_TruncatedInputsNeverThrow()
    {
        string src = "function run()\n    print(1)\nend\n";
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
        ConcreteSyntaxTree happy = _pack.Parse(SourceText.From("function a() end\n"));
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
            Task.Run(() => a = _pack.Parse(SourceText.From("function alpha() end\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("function beta() end\n"))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "beta");
    }
}
