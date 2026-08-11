using System.Collections.Generic;

namespace TgrDevelopments.Cst;

/// <summary>Registry of language packs, outline providers, and extractors.</summary>
public interface ILanguagePackRegistry
{
    /// <summary>Registers a language pack.</summary>
    void Register(ILanguagePack pack);

    /// <summary>Registers an outline provider.</summary>
    void RegisterOutline(IOutlineProvider provider);

    /// <summary>Registers a structural extractor.</summary>
    void RegisterExtractor(IStructuralExtractor extractor);

    /// <summary>Resolves a pack for a path or file name.</summary>
    ILanguagePack? ResolvePack(string pathOrFileName);

    /// <summary>Resolves an outline provider by language id.</summary>
    IOutlineProvider? ResolveOutline(string languageId);

    /// <summary>Resolves an extractor by language id.</summary>
    IStructuralExtractor? ResolveExtractor(string languageId);

    /// <summary>Registered packs in registration order.</summary>
    IReadOnlyList<ILanguagePack> Packs { get; }
}
