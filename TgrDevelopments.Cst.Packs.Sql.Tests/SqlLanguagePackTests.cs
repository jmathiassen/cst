using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Sql;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Sql.Tests;

public class SqlLanguagePackTests : StructuralPackTestBase
{
    private readonly SqlLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "CREATE TABLE t (id INT);\n" + new string(' ', 100);
    protected override string FuzzSource => """
CREATE TABLE users (
  id INT PRIMARY KEY
);
CREATE VIEW active_users AS SELECT * FROM users;
""";
    protected override string BrokenSource => "CREATE TABLE (\n";

    [Fact]
    public void Parse_Happy_TableAndViewLabels()
    {
        string src = """
CREATE TABLE users (
  id INT PRIMARY KEY
);
CREATE VIEW active_users AS SELECT * FROM users;
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Equal(2, outline.Count);
        Assert.Equal("table", outline[0].Kind);
        Assert.Equal("users", outline[0].Label);
        Assert.Equal(1, outline[0].LineSpan.Start.Line);
        Assert.Equal(3, outline[0].LineSpan.End.Line);
        Assert.Equal("view", outline[1].Kind);
        Assert.Equal("active_users", outline[1].Label);
    }

    [Fact]
    public void Parse_StringSafety_NoFalseTable()
    {
        string src = "SELECT 'CREATE TABLE Fake';\nCREATE TABLE Real (id INT);\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label == "Fake");
        Assert.Contains(outline, static o => o.Label == "Real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseTable()
    {
        string src = "-- CREATE TABLE Fake\nCREATE TABLE Real (id INT);\n";
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
            Task.Run(() => a = _pack.Parse(SourceText.From("CREATE TABLE Alpha (id INT);\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("CREATE TABLE Beta (id INT);\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "Alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "Beta");
    }
}
