using System;
using System.Collections.Generic;
using System.IO;

namespace TgrDevelopments.Cst;

/// <summary>Default mutable registry for hosts.</summary>
public sealed class LanguagePackRegistry : ILanguagePackRegistry
{
    private readonly List<ILanguagePack> _packs = [];
    private readonly Dictionary<string, IOutlineProvider> _outlines =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IStructuralExtractor> _extractors =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyList<ILanguagePack> Packs => _packs;

    /// <inheritdoc />
    public void Register(ILanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        _packs.Add(pack);
    }

    /// <inheritdoc />
    public void RegisterOutline(IOutlineProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _outlines[provider.LanguageId] = provider;
    }

    /// <inheritdoc />
    public void RegisterExtractor(IStructuralExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        _extractors[extractor.LanguageId] = extractor;
    }

    /// <inheritdoc />
    public ILanguagePack? ResolvePack(string pathOrFileName)
    {
        ArgumentNullException.ThrowIfNull(pathOrFileName);

        string fileName = Path.GetFileName(pathOrFileName);

        for (int i = 0; i < _packs.Count; i++)
        {
            ILanguagePack pack = _packs[i];
            if (pack.OwnsPath(pathOrFileName) || pack.OwnsPath(fileName))
                return pack;
        }

        string extension = Path.GetExtension(pathOrFileName);
        if (extension.Length == 0)
            return null;

        for (int i = 0; i < _packs.Count; i++)
        {
            ILanguagePack pack = _packs[i];
            IReadOnlyList<string> extensions = pack.FileExtensions;
            for (int j = 0; j < extensions.Count; j++)
            {
                if (string.Equals(extensions[j], extension, StringComparison.OrdinalIgnoreCase))
                    return pack;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public IOutlineProvider? ResolveOutline(string languageId)
    {
        ArgumentNullException.ThrowIfNull(languageId);
        if (_outlines.TryGetValue(languageId, out IOutlineProvider? provider))
            return provider;
        return null;
    }

    /// <inheritdoc />
    public IStructuralExtractor? ResolveExtractor(string languageId)
    {
        ArgumentNullException.ThrowIfNull(languageId);
        if (_extractors.TryGetValue(languageId, out IStructuralExtractor? extractor))
            return extractor;
        return null;
    }
}
