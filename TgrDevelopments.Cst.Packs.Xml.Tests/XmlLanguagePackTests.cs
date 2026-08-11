using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Xml;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Xml.Tests;

public class XmlLanguagePackTests : StructuralPackTestBase
{
    private readonly XmlLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => @"<Project Sdk=""""Microsoft.NET.Sdk"""">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
";
    protected override string FuzzSource => @"<Project Sdk=""""Microsoft.NET.Sdk"""">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
";
    protected override string BrokenSource => "<root><child";

    [Fact]
    public void Parse_Happy_OutlineHasLabelsLevelsAndLines()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(@"<Project Sdk=""""Microsoft.NET.Sdk"""">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
"));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.NotEmpty(outline);
        Assert.All(outline, static o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Kind));
            Assert.False(string.IsNullOrWhiteSpace(o.Label));
            Assert.True(o.Level >= 1);
            Assert.True(o.LineSpan.Start.Line >= 1);
            Assert.True(o.LineSpan.End.Line >= o.LineSpan.Start.Line);
            Assert.True(o.Span.Length >= 0);
        });
    }

    [Fact]
    public void Parse_IncludeAttr_OnOpenAndSelfClose()
    {
        string src = """
<P>
  <Compile Include="A.cs" />
  <Compile Include="B.cs"></Compile>
</P>
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        OutlineNode root = Assert.Single(outline);
        Assert.Equal(2, root.Children.Count);
        Assert.Contains(root.Children, static o => o.Label.Contains("A.cs", System.StringComparison.Ordinal));
        Assert.Contains(root.Children, static o => o.Label.Contains("B.cs", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_NoThrowDistinctTrees()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From(@"<Project Sdk=""""Microsoft.NET.Sdk"""">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
"))),
            Task.Run(() => b = _pack.Parse(SourceText.From(""))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        IReadOnlyList<OutlineNode> oA = _pack.GetOutline(a!);
        IReadOnlyList<OutlineNode> oB = _pack.GetOutline(b!);
        Assert.NotEmpty(oA);
        Assert.Empty(oB);
    }

    [Fact]
    public void Parse_StringSafety_NoFalseElement()
    {
        string src = "<a attr=\">\"></a>\n<real></real>\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseElement()
    {
        string src = "<!-- <fake></fake> -->\n<real></real>\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }
}
