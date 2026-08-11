using System;
using System.Collections.Generic;
using System.Threading;

namespace TgrDevelopments.Cst;

/// <summary>Recommended host entry façade over a language pack registry.</summary>
public sealed class CstService
{
    private readonly ILanguagePackRegistry _registry;

    /// <summary>Creates a service bound to the given registry.</summary>
    /// <param name="registry">Pack registry.</param>
    public CstService(ILanguagePackRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// Tries to parse using a pack resolved from <paramref name="path"/>.
    /// Returns null only when no pack resolves; parse failures still return a tree.
    /// </summary>
    public ConcreteSyntaxTree? TryParse(
        string path,
        SourceText source,
        ParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(source);

        ILanguagePack? pack = _registry.ResolvePack(path);
        if (pack is null)
            return null;

        return pack.Parse(source, options, cancellationToken);
    }

    /// <summary>Gets an outline for a tree, or an empty list when no provider is registered.</summary>
    public IReadOnlyList<OutlineNode> GetOutline(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);

        IOutlineProvider? provider = _registry.ResolveOutline(tree.LanguageId);
        if (provider is null)
            return Array.Empty<OutlineNode>();

        return provider.GetOutline(tree, cancellationToken);
    }

    /// <summary>Tries to extract structure; null when no extractor is registered.</summary>
    public StructuralExtract? TryExtract(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);

        IStructuralExtractor? extractor = _registry.ResolveExtractor(tree.LanguageId);
        if (extractor is null)
            return null;

        return extractor.Extract(tree, cancellationToken);
    }
}
