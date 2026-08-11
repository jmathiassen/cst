using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.JavaScript;

/// <summary>Structural extractor for JavaScript — decls from pack outline (RD), calls via token-safe scan.</summary>
public sealed class JavaScriptStructuralExtractor : IStructuralExtractor
{
    private readonly JavaScriptLanguagePack _pack = new();

    /// <inheritdoc />
    public string LanguageId => "javascript";

    /// <inheritdoc />
    public StructuralExtract Extract(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();

        string text = tree.Source.Text;
        if (ParseHelpers.IsNullOrWhiteSpace(text))
        {
            return new StructuralExtract(
                LanguageId,
                quality: SyntaxQuality.Structural);
        }

        LineMap map = new(text);
        IReadOnlyList<OutlineNode> outline = tree.CachedOutline ?? _pack.GetOutline(tree, cancellationToken);
        List<DeclSymbol> decls = ExtractHelpers.DeclsFromOutline(outline, map);
        List<(int Start, int End, string Name)> ranges = ExtractHelpers.DeclRanges(decls);

        List<CallSite> calls = ExtractHelpers.ScanCalls(
            text,
            map,
            offset => ExtractHelpers.EnclosingName(ranges, offset));

        return new StructuralExtract(
            LanguageId,
            decls,
            calls,
            quality: SyntaxQuality.Structural);
    }
}
