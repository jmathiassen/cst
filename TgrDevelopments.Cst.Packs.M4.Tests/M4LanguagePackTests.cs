using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.M4;
using TgrDevelopments.Cst.TestHelpers;

namespace TgrDevelopments.Cst.Packs.M4.Tests;

public class M4LanguagePackTests : StructuralPackTestBase
{
    private readonly M4LanguagePack _pack = new();

    protected override ILanguagePack Pack => _pack;
    protected override IOutlineProvider OutlineProvider => _pack;
    protected override SyntaxQuality ExpectedQuality => SyntaxQuality.Structural;

    protected override string[]? ExpectedBrokenDiagnosticCodes => ["M4001"];

    protected override string OversizeSource => "AC_INIT([pkg], [1.0])\n" + new string('x', 100);

    protected override string FuzzSource => """
dnl Copyright notice
AC_DEFUN([gl_BUILD_TO_HOST],
[
  AC_REQUIRE([gl_BUILD_TO_HOST_INIT])
  AC_CONFIG_COMMANDS([build-to-host], [eval $gl_config_gt | $SHELL], [gl_config_gt=true])
])
AC_INIT([pkg], [1.0])
m4_include([m4/extra.m4])
""";

    protected override string BrokenSource => "AC_DEFUN([x], [\n";

    private static readonly string HappySource = """
dnl Copyright notice
AC_DEFUN([gl_BUILD_TO_HOST],
[
  AC_REQUIRE([gl_BUILD_TO_HOST_INIT])
  AC_CONFIG_COMMANDS([build-to-host], [eval $gl_config_gt | $SHELL], [gl_config_gt=true])
])
AC_INIT([pkg], [1.0])
m4_include([m4/extra.m4])
""";

    [Fact]
    public void Parse_Happy_WorkedExampleOutline()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(HappySource));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Equal(3, outline.Count);

        OutlineNode macro = outline[0];
        Assert.Equal("macro", macro.Kind);
        Assert.Equal("gl_BUILD_TO_HOST", macro.Label);
        Assert.Equal(1, macro.Level);
        Assert.Equal(2, macro.LineSpan.Start.Line);
        Assert.Equal(6, macro.LineSpan.End.Line);
        Assert.Equal(2, macro.Children.Count);
        Assert.Equal("call", macro.Children[0].Kind);
        Assert.Equal("AC_REQUIRE", macro.Children[0].Label);
        Assert.Equal(2, macro.Children[0].Level);
        Assert.Equal(4, macro.Children[0].LineSpan.Start.Line);
        Assert.Equal("call", macro.Children[1].Kind);
        Assert.Equal("AC_CONFIG_COMMANDS", macro.Children[1].Label);
        Assert.Equal(2, macro.Children[1].Level);
        Assert.Equal(5, macro.Children[1].LineSpan.Start.Line);

        Assert.Equal("call", outline[1].Kind);
        Assert.Equal("AC_INIT", outline[1].Label);
        Assert.Equal(1, outline[1].Level);
        Assert.Equal(7, outline[1].LineSpan.Start.Line);

        Assert.Equal("include", outline[2].Kind);
        Assert.Equal("m4/extra.m4", outline[2].Label);
        Assert.Equal(1, outline[2].Level);
        Assert.Equal(8, outline[2].LineSpan.Start.Line);
    }

    [Fact]
    public void Parse_Happy_TreeKindsAndArguments()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(HappySource));
        Assert.Equal("document", tree.Root.Kind);
        SyntaxNode def = tree.Root.Children.First(static n => n.Kind == "macro_definition");
        Assert.Equal("gl_BUILD_TO_HOST", def.Name);
        Assert.True(def.Children.Count >= 2);
        Assert.Contains(def.Children, static c => c.Kind == "argument");

        SyntaxNode? commands = FindByName(tree.Root, "AC_CONFIG_COMMANDS");
        Assert.NotNull(commands);
        Assert.Equal("macro_call", commands!.Kind);
        Assert.Equal("true", commands.Properties?["high_value"]);
        Assert.Equal("config_commands", commands.Properties?["role"]);
        Assert.True(commands.Children.Count >= 2);
    }

    [Fact]
    public void Parse_NameSpan_DefAndCalls()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(HappySource));
        string text = HappySource;

        SyntaxNode def = tree.Root.Children.First(static n => n.Kind == "macro_definition");
        Assert.NotNull(def.NameSpan);
        Assert.Equal("gl_BUILD_TO_HOST", Slice(text, def.NameSpan!.Value));

        SyntaxNode init = tree.Root.Children.First(static n => n.Name == "AC_INIT");
        Assert.NotNull(init.NameSpan);
        Assert.Equal("AC_INIT", Slice(text, init.NameSpan!.Value));

        SyntaxNode? req = FindByName(tree.Root, "AC_REQUIRE");
        Assert.NotNull(req);
        Assert.NotNull(req!.NameSpan);
        Assert.Equal("AC_REQUIRE", Slice(text, req.NameSpan!.Value));
    }

    [Fact]
    public void Parse_Nested_ChildLevels()
    {
        string src = """
AC_DEFUN([outer],
[
  AC_REQUIRE([inner])
  AC_INIT([p], [1])
])
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Single(outline);
        Assert.Equal("macro", outline[0].Kind);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal(2, outline[0].Children.Count);
        Assert.All(outline[0].Children, static c => Assert.Equal(2, c.Level));
        Assert.All(outline[0].Children, c =>
        {
            Assert.True(c.LineSpan.Start.Line >= outline[0].LineSpan.Start.Line);
            Assert.True(c.LineSpan.End.Line <= outline[0].LineSpan.End.Line);
        });
    }

    [Fact]
    public void Parse_StringSafety_NoFalseMacro()
    {
        string src = "x=\"AC_DEFUN([nope], [])\"\nAC_INIT([real], [1])\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label.Contains("nope"));
        Assert.Contains(outline, static o => o.Label == "AC_INIT");
    }

    [Fact]
    public void Parse_QuoteSafety_KeywordWithoutParen_NoCall()
    {
        string src = "AC_DEFUN([x], [ AC_INIT fake ])\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Single(outline);
        Assert.Equal("macro", outline[0].Kind);
        Assert.Empty(outline[0].Children);
        Assert.DoesNotContain(Flatten(outline), static o => o.Kind == "call" && o.Label == "AC_INIT");
    }

    [Fact]
    public void Parse_CommentSafety_DnlAndHash()
    {
        string src = "dnl AC_DEFUN([nope], [])\n# AC_INIT([x],[1])\nAC_INIT([real], [1])\n";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.DoesNotContain(outline, static o => o.Label.Contains("nope"));
        Assert.Contains(outline, static o => o.Label == "AC_INIT");
        Assert.Single(outline);
    }

    [Fact]
    public void Parse_ConfigureAc_TopLevelCalls()
    {
        string src = """
AC_INIT([pkg], [1.0])
AC_CONFIG_SRCDIR([src/main.c])
AC_CONFIG_FILES([Makefile])
AC_OUTPUT
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == "AC_INIT");
        Assert.Contains(outline, static o => o.Label == "AC_CONFIG_SRCDIR");
        Assert.Contains(outline, static o => o.Label == "AC_CONFIG_FILES");
    }

    [Fact]
    public void Parse_BuildToHostShape_ConfigCommandsPresent()
    {
        string src = """
AC_DEFUN([gl_BUILD_TO_HOST],
[
  AC_REQUIRE([gl_BUILD_TO_HOST_INIT])
  AC_CONFIG_COMMANDS([build-to-host], [eval $gl_config_gt | $SHELL], [gl_config_gt=true])
])
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        Assert.NotNull(tree.Root);
        Assert.DoesNotContain(tree.Diagnostics, static d => d.Code == "M4001");
        SyntaxNode? commands = FindByName(tree.Root, "AC_CONFIG_COMMANDS");
        Assert.NotNull(commands);
        Assert.True(commands!.Span.Length > 20);
        Assert.Contains("eval", Slice(src, commands.Span));
    }

    [Fact]
    public void Parse_CaseInDefun_ConfigCommandsAfterBareParen()
    {
        string src = """
AC_DEFUN([outer],
[
  AC_REQUIRE([a])
  case $x in
    y) z=1 ;;
  esac
  AC_CONFIG_COMMANDS([n], [true], [true])
])
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.DoesNotContain(tree.Diagnostics, static d => d.Code == "M4001");
        Assert.Single(outline);
        Assert.Equal("macro", outline[0].Kind);
        Assert.Contains(outline[0].Children, static c => c.Label == "AC_REQUIRE");
        Assert.Contains(outline[0].Children, static c => c.Label == "AC_CONFIG_COMMANDS");
        SyntaxNode? commands = FindByName(tree.Root, "AC_CONFIG_COMMANDS");
        Assert.NotNull(commands);
    }

    [Fact]
    public void Parse_ConfigureAc_WithCase_LaterCallsSurvive()
    {
        string src = """
AC_INIT([pkg], [1.0])
case $host_os in
  mingw*) is_w32=yes ;;
  *) is_w32=no ;;
esac
AC_CONFIG_FILES([Makefile])
AC_CONFIG_COMMANDS([post], [true], [])
""";
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(_pack.Parse(SourceText.From(src)));
        Assert.Contains(outline, static o => o.Label == "AC_INIT");
        Assert.Contains(outline, static o => o.Label == "AC_CONFIG_FILES");
        Assert.Contains(outline, static o => o.Label == "AC_CONFIG_COMMANDS");
    }

    [Fact]
    public void Parse_QuoteBody_ApostropheInDnl_DoesNotBreakBalance()
    {
        string src = """
AC_DEFUN([gl_VIS],
[
  dnl don't want to break on apostrophe
  AC_REQUIRE([AC_PROG_CC])
  AC_SUBST([CFLAG_VISIBILITY])
])
""";
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(src));
        Assert.DoesNotContain(tree.Diagnostics, static d => d.Code == "M4001");
        Assert.False(tree.IsPartial);
        IReadOnlyList<OutlineNode> outline = _pack.GetOutline(tree);
        Assert.Single(outline);
        Assert.Equal("gl_VIS", outline[0].Label);
        Assert.Contains(outline[0].Children, static c => c.Label == "AC_REQUIRE");
        Assert.Contains(outline[0].Children, static c => c.Label == "AC_SUBST");
    }

    [Fact]
    public void OwnsPath_ConfigureAcAndM4_True()
    {
        Assert.True(_pack.OwnsPath("configure.ac"));
        Assert.True(_pack.OwnsPath("path/to/configure.in"));
        Assert.True(_pack.OwnsPath("aclocal.m4"));
        Assert.True(_pack.OwnsPath("m4/foo.m4"));
        Assert.True(_pack.OwnsPath("FOO.M4"));
    }

    [Fact]
    public void OwnsPath_BareConfigure_False()
    {
        Assert.False(_pack.OwnsPath("configure"));
        Assert.False(_pack.OwnsPath("src/configure"));
        Assert.False(_pack.OwnsPath("Makefile.am"));
    }

    [Fact]
    public async Task Parse_ConcurrentSharedInstance_DistinctOutlines()
    {
        ConcreteSyntaxTree? a = null;
        ConcreteSyntaxTree? b = null;
        await Task.WhenAll(
            Task.Run(() => a = _pack.Parse(SourceText.From("AC_INIT([alpha], [1])\n"))),
            Task.Run(() => b = _pack.Parse(SourceText.From("AC_INIT([beta], [1])\n"))));
        Assert.Contains(_pack.GetOutline(a!), static o => o.Label == "AC_INIT");
        Assert.Contains(_pack.GetOutline(b!), static o => o.Label == "AC_INIT");
        Assert.Contains(a!.Root.Children, static n => n.Properties is not null && n.Properties.GetValueOrDefault("first_arg")?.Contains("alpha") == true);
        Assert.Contains(b!.Root.Children, static n => n.Properties is not null && n.Properties.GetValueOrDefault("first_arg")?.Contains("beta") == true);
    }

    private static string Slice(string text, TextSpan span)
        => text.Substring(span.Start, span.Length);

    private static SyntaxNode? FindByName(SyntaxNode node, string name)
    {
        if (node.Name == name)
            return node;
        foreach (SyntaxNode child in node.Children)
        {
            SyntaxNode? found = FindByName(child, name);
            if (found is not null)
                return found;
        }
        return null;
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
