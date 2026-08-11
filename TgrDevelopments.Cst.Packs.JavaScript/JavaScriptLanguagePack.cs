using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.JavaScript;

/// <summary>JavaScript language pack (lexer → recursive-descent outline).</summary>
public sealed class JavaScriptLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".js", ".mjs", ".cjs"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "javascript";

    /// <inheritdoc />
    public string DisplayName => "JavaScript";

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
        SourceText effective = ParseHelpers.ApplyMaxChars(
            source, options, out string text, diagnostics, out bool isPartial);

        if (ParseHelpers.IsNullOrWhiteSpace(text))
            return TextUtil.EmptyTree(LanguageId, effective, "program", diagnostics, DefaultQuality, isPartial);

        List<SyntaxNode> children = ParseDocument(text, diagnostics, ref isPartial, cancellationToken, out List<OutlineNode> outlines);
        return TextUtil.Tree(LanguageId, effective, "program", text, children, diagnostics, DefaultQuality, isPartial, outlines);
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

    private static List<SyntaxNode> ParseDocument(
        string text,
        List<ParseDiagnostic> diagnostics,
        ref bool isPartial,
        CancellationToken ct,
        out List<OutlineNode> outlines)
    {
        JsLexer lexer = new(text, diagnostics, ct);
        IReadOnlyList<Token> tokens = lexer.Tokenize();
        if (lexer.IsPartial)
            isPartial = true;

        JsParser parser = new(text, tokens, diagnostics, ct);
        List<SyntaxNode> children = parser.ParseTopLevel(out outlines);
        if (parser.IsPartial)
            isPartial = true;
        return children;
    }
}
