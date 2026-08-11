using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Vb;

/// <summary>Registration helpers for the Vb pack.</summary>
public static class VbRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        VbLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
