using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Perl;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.Perl.Tests;

public class PerlLanguagePackTests : StructuralPackTestBase
{
    private readonly PerlLanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string OversizeSource => "package App;\nsub alpha { }\nsub beta { }\n";
    protected override string FuzzSource => "package App;\nsub alpha { my $x = 1; }\nsub beta { my $y = 2; }\n";
    protected override string BrokenSource => "sub x {\n";

    [Fact]
    public void Parse_Happy_SubroutineAndPackage()
    {
        string src = """
package MyApp;

sub greet {
    print "hello\n";
}

sub run {
    my $x = 1;
    return $x;
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);

        Assert.Equal(3, outline.Count);
        Assert.Equal("package", outline[0].Kind);
        Assert.Equal("MyApp", outline[0].Label);
        Assert.Equal(1, outline[0].Level);

        Assert.Equal("function", outline[1].Kind);
        Assert.Equal("greet", outline[1].Label);
        Assert.Equal(1, outline[1].Level);

        Assert.Equal("function", outline[2].Kind);
        Assert.Equal("run", outline[2].Label);
        Assert.Equal(1, outline[2].Level);
    }

    [Fact]
    public void Parse_Nested_PackageContainsSub()
    {
        string src = """
package App;

sub outer {
    sub inner {
        return 1;
    }
}
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);

        OutlineNode pkg = outline[0];
        Assert.Equal("package", pkg.Kind);
        Assert.Equal("App", pkg.Label);
        Assert.Equal(1, pkg.Level);

        OutlineNode outer = outline[1];
        Assert.Equal("function", outer.Kind);
        Assert.Equal("outer", outer.Label);
        Assert.Equal(1, outer.Level);

        Assert.NotNull(outer.Children);
        OutlineNode inner = Assert.Single(outer.Children);
        Assert.Equal("function", inner.Kind);
        Assert.Equal("inner", inner.Label);
        Assert.Equal(2, inner.Level);
        Assert.True(inner.LineSpan.Start.Line >= outer.LineSpan.Start.Line);
        Assert.True(inner.LineSpan.End.Line <= outer.LineSpan.End.Line);
    }

    [Fact]
    public void Parse_StringSafety_NoFalseSub()
    {
        string src = """
sub real { }
my $s = "sub fake { }";
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_CommentSafety_NoFalseSub()
    {
        string src = """
# sub fake { }
sub real { }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_PodSafety_NoFalseSub()
    {
        string src = """
=head1 NAME

App - thing

=cut

sub real { }
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label == "NAME");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_RegexSafety_NoFalseSubInMatch()
    {
        string src = "sub real { }\nmy $x = \"hello\" =~ /sub fake/;\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_RegexSafety_MatchWithDelimiters()
    {
        string src = "sub real { }\nmy $x = \"hello\" =~ m#sub fake#;\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_RegexSafety_Substitution()
    {
        string src = "sub real { }\nmy $x = \"hello\" =~ s/sub fake//g;\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_RegexSafety_BracedMatch()
    {
        string src = "sub real { }\nmy $x = \"hello\" =~ m{sub fake};\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_RegexSafety_BracedSubstitution()
    {
        string src = "sub real { }\nmy $x = \"hello\" =~ s{sub fake}{}g;\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(outline, static o => o.Label == "fake");
        Assert.Contains(outline, static o => o.Label == "real");
    }

    [Fact]
    public void Parse_ForwardDeclaration_SubWithoutBody()
    {
        string src = "sub forward;\nsub real { }\n";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(2, outline.Count);
        Assert.Equal("forward", outline[0].Label);
        Assert.Equal("real", outline[1].Label);
    }

    [Fact]
    public void Parse_Broken_PartialWithDiagnostic()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("sub x {\n"));
        Assert.True(tree.IsPartial);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "PERL001");
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("sub alpha { }\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("sub beta { }\n"))));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "alpha");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "beta");
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
