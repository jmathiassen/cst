using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Markdown;

/// <summary>Optional structural extractor for Markdown — heading declarations.</summary>
public sealed class MarkdownStructuralExtractor : IStructuralExtractor
{
    /// <inheritdoc />
    public string LanguageId => "markdown";

    /// <inheritdoc />
    public StructuralExtract Extract(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();

        List<DeclSymbol> decls = [];
        LineMap map = new(tree.Source.Text);

        CollectHeadings(tree.Root, map, decls, cancellationToken);

        return new StructuralExtract(
            LanguageId,
            decls,
            quality: SyntaxQuality.Preview);
    }

    private static void CollectHeadings(
        SyntaxNode node,
        LineMap map,
        List<DeclSymbol> decls,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (node.Kind == "heading" && !string.IsNullOrEmpty(node.Name))
        {
            decls.Add(ExtractHelpers.Decl(
                node.Name!,
                "heading",
                node.Span,
                map,
                nameSpan: node.NameSpan));
        }

        for (int i = 0; i < node.Children.Count; i++)
            CollectHeadings(node.Children[i], map, decls, ct);
    }
}
