using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Markdown;

/// <summary>Registration helpers for the Markdown pack.</summary>
public static class MarkdownRegistration
{
    /// <summary>Registers the Markdown pack, outline provider, and optional extractor.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        MarkdownLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
        registry.RegisterExtractor(new MarkdownStructuralExtractor());
    }
}
