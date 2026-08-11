using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Css;

/// <summary>Registration helpers for the Css pack.</summary>
public static class CssRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        CssLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
