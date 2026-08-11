using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.PowerShell;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.PowerShell.Tests;

public class PowerShellLanguagePackTests : StructuralPackTestBase
{
    private readonly PowerShellLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "function a { }\n" + new string(' ', 100);
    protected override string FuzzSource => """
class Engine {
  [void] Run() { }
}
function Invoke-Main { }
""";
    protected override string BrokenSource => "class X {\n";

    [Fact]
    public void Parse_Happy_ClassMethodAndFunction()
    {
        string src = """
class Engine {
  [void] Run() { }
}
function Invoke-Main { }
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Equal(2, outline.Count);
        Assert.Equal("class", outline[0].Kind);
        Assert.Equal("Engine", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Contains(outline[0].Children, static c => c.Kind == "method" && c.Label == "Run" && c.Level == 2);
        Assert.Equal("function", outline[1].Kind);
        Assert.Equal("Invoke-Main", outline[1].Label);
    }

    [Fact]
    public void Parse_Nested_ClassContainsMethod()
    {
        string src = """
class Outer {
  [void] MethodA() { }
  [void] MethodB() { }
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        OutlineNode cls = Assert.Single(outline);
        Assert.Equal("class", cls.Kind);
        Assert.Equal("Outer", cls.Label);
        Assert.NotNull(cls.Children);
        Assert.Equal(2, cls.Children.Count);
        Assert.Equal("method", cls.Children[0].Kind);
        Assert.Equal("MethodA", cls.Children[0].Label);
        Assert.Equal(2, cls.Children[0].Level);
    }

    [Fact]
    public void Parse_StringSafety_NoFalseFunction()
    {
        string src = "$s = 'function Fake { }'\nfunction Real { }\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "Fake");
        Assert.Contains(outline, static o => o.Label == "Real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseFunction()
    {
        string src = "# comment function Fake { }\nfunction Real { }\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "Fake");
        Assert.Contains(outline, static o => o.Label == "Real");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("function Alpha { }\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("function Beta { }\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }
}
