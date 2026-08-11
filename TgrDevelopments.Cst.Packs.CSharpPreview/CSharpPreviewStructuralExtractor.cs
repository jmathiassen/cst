using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.CSharpPreview;

/// <summary>Structural extractor for csharp-preview — decls from pack outline (RD), calls via scan.</summary>
public sealed class CSharpPreviewStructuralExtractor : IStructuralExtractor
{
    private readonly CSharpPreviewLanguagePack _pack = new();

    /// <inheritdoc />
    public string LanguageId => "csharp-preview";

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
            return new StructuralExtract(LanguageId, quality: SyntaxQuality.Preview);
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
            quality: SyntaxQuality.Preview);
    }
}
