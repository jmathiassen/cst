using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Python;

/// <summary>Python language pack (lexer with INDENT/DEDENT → recursive-descent outline).</summary>
public sealed class PythonLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".py", ".pyi", ".pyw"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "python";

    /// <inheritdoc />
    public string DisplayName => "Python";

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
            return TextUtil.EmptyTree(LanguageId, effective, "module", diagnostics, DefaultQuality, isPartial);

        List<SyntaxNode> children = ParseDocument(text, diagnostics, ref isPartial, cancellationToken, out List<OutlineNode> outlines);
        return TextUtil.Tree(LanguageId, effective, "module", text, children, diagnostics, DefaultQuality, isPartial, outlines);
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
        PyLexer lexer = new(text, diagnostics, ct);
        IReadOnlyList<Token> tokens = lexer.Tokenize();
        if (lexer.IsPartial)
            isPartial = true;

        PyParser parser = new(text, tokens, diagnostics, ct);
        List<SyntaxNode> children = parser.ParseModule(out outlines);
        if (parser.IsPartial)
            isPartial = true;
        return children;
    }
}
