using System;
using System.Linq;
using System.Threading;

using Xunit;

namespace TgrDevelopments.Cst.TestHelpers;

public abstract class StructuralPackTestBase
{
    protected abstract ILanguagePack Pack { get; }
    protected abstract IOutlineProvider OutlineProvider { get; }
    protected abstract SyntaxQuality ExpectedQuality { get; }

    protected abstract string OversizeSource { get; }
    protected abstract string FuzzSource { get; }
    protected abstract string BrokenSource { get; }

    /// <summary>Diagnostic codes expected from BrokenSource. Override to assert in Parse_Broken_NoThrow.</summary>
    protected virtual string[]? ExpectedBrokenDiagnosticCodes => null;

    [Fact]
    public void Parse_Empty_OutlineEmpty()
    {
        ConcreteSyntaxTree tree = Pack.Parse(SourceText.From(""));
        Assert.NotNull(tree.Root);
        Assert.Equal(SyntaxConfidence.Syntax, tree.Confidence);
        Assert.Equal(ExpectedQuality, tree.Quality);
        Assert.Empty(OutlineProvider.GetOutline(tree));
    }

    [Fact]
    public void Parse_CancelledToken_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Pack.Parse(
            SourceText.From(" "),
            cancellationToken: cts.Token));
    }

    [Fact]
    public void Parse_Oversize_RespectsMaxChars()
    {
        string src = OversizeSource;
        ParseOptions options = new() { MaxChars = 10 };
        ConcreteSyntaxTree tree = Pack.Parse(SourceText.From(src), options);
        Assert.Contains(tree.Diagnostics, static d => d.Code == "CST_LIMIT");
        Assert.True(tree.IsPartial);
    }

    [Fact]
    public void Parse_NoThrowFuzz_TruncatedInputsNeverThrow()
    {
        string src = FuzzSource;
        for (int i = 0; i < src.Length; i++)
        {
            string cut = src[..i];
            ConcreteSyntaxTree tree = Pack.Parse(SourceText.From(cut));
            Assert.NotNull(tree.Root);
        }
    }

    [Fact]
    public void Parse_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = Pack.Parse(SourceText.From(BrokenSource));
        Assert.NotNull(tree.Root);
        Assert.True(tree.IsPartial);
        if (ExpectedBrokenDiagnosticCodes is string[] codes)
        {
            foreach (string code in codes)
                Assert.Contains(tree.Diagnostics, d => d.Code == code);
        }
    }

    [Fact]
    public void GetOutline_SharedInstance_IndependentOfLaterParse()
    {
        ConcreteSyntaxTree empty = Pack.Parse(SourceText.From(""));
        Assert.Empty(OutlineProvider.GetOutline(empty));
    }
}
