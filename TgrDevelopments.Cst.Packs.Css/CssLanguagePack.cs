using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Css;

/// <summary>CSS language pack (lexer → recursive-descent outline).</summary>
public sealed class CssLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".css"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "css";
    /// <inheritdoc />
    public string DisplayName => "CSS";
    /// <inheritdoc />
    public IReadOnlyList<string> FileExtensions => Extensions;
    /// <inheritdoc />
    public IReadOnlyList<string> FileNamePatterns => NamePatterns;
    /// <inheritdoc />
    public SyntaxQuality DefaultQuality => SyntaxQuality.Structural;

    /// <inheritdoc />
    public bool OwnsPath(string relativeOrFileName)
    {
        ArgumentNullException.ThrowIfNull(relativeOrFileName);
        return false;
    }

    /// <inheritdoc />
    public ConcreteSyntaxTree Parse(SourceText source, ParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        List<ParseDiagnostic> diagnostics = [];
        SourceText effective = ParseHelpers.ApplyMaxChars(source, options, out string text, diagnostics, out bool isPartial);
        if (ParseHelpers.IsNullOrWhiteSpace(text))
            return TextUtil.EmptyTree(LanguageId, effective, "stylesheet", diagnostics, DefaultQuality, isPartial);
        List<SyntaxNode> children = ParseDoc(text, diagnostics, ref isPartial, cancellationToken, out List<OutlineNode> outlines);
        return TextUtil.Tree(LanguageId, effective, "stylesheet", text, children, diagnostics, DefaultQuality, isPartial, outlines);
    }

    /// <inheritdoc />
    public IReadOnlyList<OutlineNode> GetOutline(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();
        if (tree.CachedOutline is not null)
            return tree.CachedOutline;
        return Array.Empty<OutlineNode>();
    }

    private static List<SyntaxNode> ParseDoc(
        string text, List<ParseDiagnostic> diagnostics, ref bool isPartial, CancellationToken ct, out List<OutlineNode> outlines)
    {
        CssLexer lexer = new(text, diagnostics, ct);
        IReadOnlyList<Token> tokens = lexer.Tokenize();
        if (lexer.IsPartial) isPartial = true;
        CssParser parser = new(text, tokens, diagnostics, ct);
        List<SyntaxNode> children = parser.Parse(out outlines);
        if (parser.IsPartial) isPartial = true;
        return children;
    }
}
