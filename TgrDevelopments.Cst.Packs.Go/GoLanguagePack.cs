using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Go;

/// <summary>Go language pack (lexer → recursive-descent outline).</summary>
public sealed class GoLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".go"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "go";

    /// <inheritdoc />
    public string DisplayName => "Go";

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
    public ConcreteSyntaxTree Parse(
        SourceText source,
        ParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        List<ParseDiagnostic> diagnostics = [];
        SourceText effective = ParseHelpers.ApplyMaxChars(source, options, out string text, diagnostics, out bool isPartial);
        if (ParseHelpers.IsNullOrWhiteSpace(text))
            return TextUtil.EmptyTree(LanguageId, effective, "source_file", diagnostics, DefaultQuality, isPartial);

        GoLexer lexer = new(text, diagnostics, cancellationToken);
        IReadOnlyList<Token> tokens = lexer.Tokenize();
        if (lexer.IsPartial)
            isPartial = true;
        GoParser parser = new(text, tokens, diagnostics, cancellationToken);
        List<SyntaxNode> children = parser.Parse(out List<OutlineNode> outlines);
        if (parser.IsPartial)
            isPartial = true;
        return TextUtil.Tree(LanguageId, effective, "source_file", text, children, diagnostics, DefaultQuality, isPartial, outlines);
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
}
