using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Env;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Env.Tests;

public class EnvLanguagePackTests : StructuralPackTestBase
{
    private readonly EnvLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "A=aaaaaaaaaaaa\n";
    protected override string FuzzSource => "# comment\nAPI_KEY=secret\nDEBUG=1\n";
    protected override string BrokenSource => "NOT_A_KEY\n";

    [Fact]
    public void Parse_Happy_KeysAndLines()
    {
        string src = """
# comment
API_KEY=secret
DEBUG=1
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);
        Assert.Equal("key", outline[0].Kind);
        Assert.Equal("API_KEY", outline[0].Label);
        Assert.Equal(2, outline[0].LineSpan.Start.Line);
        Assert.Equal("DEBUG", outline[1].Label);
        Assert.DoesNotContain(outline, static o => o.Label.Contains("secret", System.StringComparison.Ordinal));
    }

    [Fact]
    public void OwnsPath_DotEnv_True()
    {
        Assert.True(_pack.OwnsPath(".env"));
        Assert.True(_pack.OwnsPath(".env.local"));
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("A=1\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("B=2\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "A");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "B");
    }
}
