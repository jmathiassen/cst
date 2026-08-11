using System;
using System.Collections.Generic;

namespace TgrDevelopments.Cst;

/// <summary>Result of a parse. Always produced for accepted input sizes; may be partial.</summary>
public sealed class ConcreteSyntaxTree
{
    /// <summary>Language pack id that produced this tree.</summary>
    public string LanguageId { get; }

    /// <summary>Source snapshot.</summary>
    public SourceText Source { get; }

    /// <summary>Root node (never null).</summary>
    public SyntaxNode Root { get; }

    /// <summary>Diagnostics collected during parse.</summary>
    public IReadOnlyList<ParseDiagnostic> Diagnostics { get; }

    /// <summary>Always <see cref="SyntaxConfidence.Syntax"/> in v1.</summary>
    public SyntaxConfidence Confidence { get; }

    /// <summary>Quality grade for this result.</summary>
    public SyntaxQuality Quality { get; }

    /// <summary>True when recovery truncated or skipped regions.</summary>
    public bool IsPartial { get; }

    /// <summary>
    /// Outline produced during <c>Parse</c>, if the pack attached one.
    /// Packs stay stateless; hosts/providers should prefer this over re-parsing.
    /// </summary>
    public IReadOnlyList<OutlineNode>? CachedOutline { get; }

    /// <summary>Creates a concrete syntax tree.</summary>
    public ConcreteSyntaxTree(
        string languageId,
        SourceText source,
        SyntaxNode root,
        IReadOnlyList<ParseDiagnostic>? diagnostics = null,
        SyntaxConfidence confidence = SyntaxConfidence.Syntax,
        SyntaxQuality quality = SyntaxQuality.Preview,
        bool isPartial = false,
        IReadOnlyList<OutlineNode>? cachedOutline = null)
    {
        ArgumentNullException.ThrowIfNull(languageId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(root);
        LanguageId = languageId;
        Source = source;
        Root = root;
        Diagnostics = diagnostics ?? Array.Empty<ParseDiagnostic>();
        Confidence = confidence;
        Quality = quality;
        IsPartial = isPartial;
        CachedOutline = cachedOutline;
    }
}
