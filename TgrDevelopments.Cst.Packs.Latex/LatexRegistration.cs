using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Latex;

/// <summary>Registration helpers for the Latex pack.</summary>
public static class LatexRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        LatexLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
