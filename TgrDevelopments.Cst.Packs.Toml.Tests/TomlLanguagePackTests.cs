using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Toml;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Toml.Tests;

public class TomlLanguagePackTests : StructuralPackTestBase
{
    private readonly TomlLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "name = \"cst\"\n\n[meta]\nversion = 1\n";
    protected override string FuzzSource => """
name = "cst"

[meta]
version = 1
""";
    protected override string BrokenSource => "[unterminated\n";

    [Fact]
    public void Parse_Happy_KeyAndTable()
    {
        string src = """
name = "cst"

[meta]
version = 1
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Kind == "key" && o.Label == "name" && o.Level == 1);
        OutlineNode meta = Assert.Single(outline, static o => o.Kind == "table" && o.Label == "meta");
        Assert.Contains(meta.Children, static c => c.Kind == "key" && c.Label == "version" && c.Level == 2);
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("alpha = 1\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("beta = 1\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "beta");
    }

    [Fact]
    public void Parse_StringSafety_NoFalseTable()
    {
        string src = "title = \"[meta]\"\n\n[real]\nversion = 1\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "meta");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseTable()
    {
        string src = "# [meta]\n\n[real]\nversion = 1\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "meta");
        Assert.Contains(outline, static o => o.Label == "real");
    }
}
