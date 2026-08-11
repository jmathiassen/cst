using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Proto;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Proto.Tests;

public class ProtoLanguagePackTests : StructuralPackTestBase
{
    private readonly ProtoLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "message X { string s = 1; string t = 2; }\n";
    protected override string FuzzSource => """
syntax = "proto3";
package demo;
message User { string name = 1; }
service UserService {
  rpc Get(User) returns (User);
}
""";
    protected override string BrokenSource => "message X {\n";

    [Fact]
    public void Parse_Happy_PackageMessageService()
    {
        string src = """
syntax = "proto3";
package demo;
message User { string name = 1; }
service UserService {
  rpc Get(User) returns (User);
}
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Kind == "package" && o.Label == "demo");
        Assert.Contains(outline, static o => o.Kind == "message" && o.Label == "User");
        OutlineNode svc = Assert.Single(outline, static o => o.Kind == "service" && o.Label == "UserService");
        Assert.Equal(4, svc.LineSpan.Start.Line);
        Assert.Equal(6, svc.LineSpan.End.Line);
    }

    [Fact]
    public void Parse_StringSafety_NoFalseMessage()
    {
        string src = "syntax = \"proto3\";\n// message Fake {}\nmessage Real { string x = 1; }\n";
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
            Task.Run(() => a = _pack.Parse(SourceText.From("message Alpha { string x = 1; }\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("message Beta { string x = 1; }\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }
}
