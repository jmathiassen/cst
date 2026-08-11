using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.M4;

/// <summary>M4 / Autoconf language pack (lexer -> recursive-descent structure + outline).</summary>
public sealed class M4LanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".m4"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "m4";

    /// <inheritdoc />
    public string DisplayName => "M4 / Autoconf";

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
        string name = Path.GetFileName(relativeOrFileName);
        if (name.Equals("configure.ac", StringComparison.OrdinalIgnoreCase)
            || name.Equals("configure.in", StringComparison.OrdinalIgnoreCase)
            || name.Equals("aclocal.m4", StringComparison.OrdinalIgnoreCase))
            return true;
        return name.EndsWith(".m4", StringComparison.OrdinalIgnoreCase);
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
            return TextUtil.EmptyTree(LanguageId, effective, "document", diagnostics, DefaultQuality, isPartial);

        List<SyntaxNode> children = ParseDocument(text, diagnostics, ref isPartial, cancellationToken, out List<OutlineNode> outlines);
        return TextUtil.Tree(LanguageId, effective, "document", text, children, diagnostics, DefaultQuality, isPartial, outlines);
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
        M4Lexer lexer = new(text, diagnostics, ct);
        IReadOnlyList<Token> tokens = lexer.Tokenize();
        if (lexer.IsPartial)
            isPartial = true;
        M4Parser parser = new(text, tokens, diagnostics, ct);
        List<SyntaxNode> children = parser.Parse(out outlines);
        if (parser.IsPartial)
            isPartial = true;
        return children;
    }
}
