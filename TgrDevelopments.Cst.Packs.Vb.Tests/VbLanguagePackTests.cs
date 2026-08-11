using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Vb;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Vb.Tests;

public class VbLanguagePackTests : StructuralPackTestBase
{
    private readonly VbLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "Class Alpha\nEnd Class\n";
    protected override string FuzzSource => """
Namespace App
  Public Class Engine
    Public Sub Run()
    End Sub
  End Class
End Namespace
""";
    protected override string BrokenSource => "Class X\n";

    [Fact]
    public void Parse_Happy_NestedNamespaceClassSub()
    {
        string src = """
Namespace App
  Public Class Engine
    Public Sub Run()
    End Sub
  End Class
End Namespace
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        OutlineNode ns = Assert.Single(outline);
        Assert.Equal("namespace", ns.Kind);
        Assert.Equal("App", ns.Label);
        Assert.Equal(1, ns.Level);
        OutlineNode cls = Assert.Single(ns.Children);
        Assert.Equal("class", cls.Kind);
        Assert.Equal("Engine", cls.Label);
        Assert.Equal(2, cls.Level);
        Assert.Contains(cls.Children, static c => c.Kind == "sub" && c.Label == "Run" && c.Level == 3);
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseClass()
    {
        string src = "' Class Fake\nClass Real\nEnd Class\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(Flatten(outline), static o => o.Label == "Fake");
        Assert.Contains(Flatten(outline), static o => o.Label == "Real");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("Class Alpha\nEnd Class\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("Class Beta\nEnd Class\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }

    private static IEnumerable<OutlineNode> Flatten(IEnumerable<OutlineNode> nodes)
    {
        foreach (OutlineNode n in nodes)
        {
            yield return n;
            foreach (OutlineNode c in Flatten(n.Children))
                yield return c;
        }
    }
}
