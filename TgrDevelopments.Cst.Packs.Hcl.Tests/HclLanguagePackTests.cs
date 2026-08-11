using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Hcl;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Hcl.Tests;

public class HclLanguagePackTests : StructuralPackTestBase
{
    private readonly HclLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "variable \"a\" {}\n" + new string(' ', 100);
    protected override string FuzzSource => """
variable "region" {
  type = string
}
resource "aws_instance" "web" {
  ami = "x"
}
""";
    protected override string BrokenSource => "resource \"x\" {\n";

    [Fact]
    public void Parse_Happy_VariableAndResource()
    {
        string src = """
variable "region" {
  type = string
}
resource "aws_instance" "web" {
  ami = "x"
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Equal(2, outline.Count);
        Assert.Equal("variable", outline[0].Kind);
        Assert.Contains("region", outline[0].Label);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);
        Assert.Equal(3, outline[0].LineSpan.End.Line);
        Assert.Equal("resource", outline[1].Kind);
        Assert.Contains("aws_instance", outline[1].Label);
        Assert.Contains("web", outline[1].Label);
    }

    [Fact]
    public void Parse_StringSafety_NoFalseResource()
    {
        string src = "x = \"resource 'aws_instance' 'fake' {}\"\nvariable \"real\" {}\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label.Contains("fake"));
        Assert.Contains(outline, static o => o.Label.Contains("real"));
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseResource()
    {
        string src = "# resource \"x\" {}\nvariable \"real\" {}\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label.Contains("real"));
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("variable \"alpha\" {}\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("variable \"beta\" {}\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label.Contains("alpha"));
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label.Contains("beta"));
    }
}
