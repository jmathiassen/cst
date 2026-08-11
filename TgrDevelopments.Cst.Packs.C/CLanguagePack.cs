using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.C;

/// <summary>C language pack (lexer → recursive-descent outline).</summary>
/// <remarks>Non-goals: preprocessor expansion/evaluation, K&amp;R function definitions (truncated body on old-style param lists), GNU C <c>__attribute__</c> expressions, <c>_Generic</c> selection.</remarks>
public sealed class CLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".c", ".h"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "c";

    /// <inheritdoc />
    public string DisplayName => "C";

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
            return TextUtil.EmptyTree(LanguageId, effective, "translation_unit", diagnostics, DefaultQuality, isPartial);

        CLexer lexer = new(text, cancellationToken);
        IReadOnlyList<Token> tokens = lexer.Tokenize();
        if (lexer.IsPartial)
            isPartial = true;
        CParser parser = new(text, tokens, diagnostics, cancellationToken);
        List<SyntaxNode> children = parser.Parse(out List<OutlineNode> outlines);
        if (parser.IsPartial)
            isPartial = true;
        return TextUtil.Tree(LanguageId, effective, "translation_unit", text, children, diagnostics, DefaultQuality, isPartial, outlines);
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
