using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Ruby;

public sealed class RubyLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".rb", ".rake"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    public string LanguageId => "ruby";
    public string DisplayName => "Ruby";
    public IReadOnlyList<string> FileExtensions => Extensions;
    public IReadOnlyList<string> FileNamePatterns => NamePatterns;
    public SyntaxQuality DefaultQuality => SyntaxQuality.Structural;

    public bool OwnsPath(string relativeOrFileName)
    {
        ArgumentNullException.ThrowIfNull(relativeOrFileName);
        return false;
    }

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
            return TextUtil.EmptyTree(LanguageId, effective, "chunk", diagnostics, DefaultQuality, isPartial);

        RubyLexer lexer = new(text, diagnostics, cancellationToken);
        IReadOnlyList<Token> tokens = lexer.Tokenize();
        if (lexer.IsPartial)
            isPartial = true;
        LineMap map = new(text);
        RubyParser parser = new(text, tokens, map, diagnostics, cancellationToken);
        List<SyntaxNode> children = parser.Parse(out List<OutlineNode> outlines);
        if (parser.IsPartial)
            isPartial = true;
        return TextUtil.Tree(LanguageId, effective, "chunk", text, children, diagnostics, DefaultQuality, isPartial, outlines);
    }

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
