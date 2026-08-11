using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Ini;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Ini.Tests;

public class IniLanguagePackTests : StructuralPackTestBase
{
    private readonly IniLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "[section]\nkey=aaaaaaaaaaaa\n";
    protected override string FuzzSource => "[database]\nhost=localhost\n\n[app]\nname=demo\n";
    protected override string BrokenSource => "[unterminated\nkey=1\n";

    [Fact]
    public void Parse_Happy_SectionsKeysNested()
    {
        string src = """
[database]
host=localhost

[app]
name=demo
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);
        Assert.Equal("section", outline[0].Kind);
        Assert.Equal("database", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Contains(outline[0].Children, static c => c.Kind == "key" && c.Label == "host" && c.Level == 2);
        Assert.Equal("section", outline[1].Kind);
        Assert.Equal("app", outline[1].Label);
        Assert.Contains(outline[1].Children, static c => c.Label == "name");
    }

    [Fact]
    public void OwnsPath_EnvFile_DoesNotOwn()
    {
        Assert.False(_pack.OwnsPath(".env"));
        Assert.False(_pack.OwnsPath("config.env"));
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("[alpha]\nx=1\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("[beta]\ny=2\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "beta");
    }
}
