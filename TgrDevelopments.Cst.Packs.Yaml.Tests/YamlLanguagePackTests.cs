using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Yaml;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Yaml.Tests;

public class YamlLanguagePackTests : StructuralPackTestBase
{
    private readonly YamlLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "name: cst\npacks:\n  - markdown\nmeta:\n  version: 1\n";
    protected override string FuzzSource => """
name: cst
packs:
  - markdown
meta:
  version: 1
""";
    protected override string BrokenSource => "key:\n";

    [Fact]
    public void Parse_Happy_KeysNested()
    {
        string src = """
name: cst
packs:
  - markdown
meta:
  version: 1
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Kind == "key" && o.Label == "name" && o.Level == 1);
        OutlineNode packs = Assert.Single(outline, static o => o.Label == "packs");
        Assert.Contains(packs.Children, static c => c.Kind == "item" && c.Label == "markdown" && c.Level == 2);
        OutlineNode meta = Assert.Single(outline, static o => o.Label == "meta");
        Assert.Contains(meta.Children, static c => c.Kind == "key" && c.Label == "version" && c.Level == 2);
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("alpha: 1\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("beta: 1\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "beta");
    }

    [Fact]
    public void Parse_StringSafety_NoFalseKey()
    {
        string src = "'key: value'\nreal: 1\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseKey()
    {
        string src = "# key: value\nreal: 1\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == "real");
    }
}
