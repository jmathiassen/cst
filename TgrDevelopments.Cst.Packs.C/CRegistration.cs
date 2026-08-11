using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.C;

/// <summary>Registration helpers for the C pack.</summary>
public static class CRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        CLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
