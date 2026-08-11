using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.TypeScript;

/// <summary>Structural extractor for TypeScript — decls from pack outline (RD), calls via token-safe scan.</summary>
public sealed class TypeScriptStructuralExtractor : IStructuralExtractor
{
    private readonly TypeScriptLanguagePack _pack = new();

    /// <inheritdoc />
    public string LanguageId => "typescript";

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

        List<CallSite> rawCalls = ExtractHelpers.ScanCalls(
            text,
            map,
            offset => ExtractHelpers.EnclosingName(ranges, offset));

        // Drop calls that look like type-position (inside interface/type alias bodies is rare in scan;
        // filter known type-only keywords as callee).
        List<CallSite> calls = [];
        foreach (CallSite c in rawCalls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (c.CalleeName is "string" or "number" or "boolean" or "any" or "void" or "never" or "unknown")
                continue;
            calls.Add(c);
        }

        return new StructuralExtract(
            LanguageId,
            decls,
            calls,
            quality: SyntaxQuality.Structural);
    }
}
