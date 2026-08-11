using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Odin;

/// <summary>Registration helpers for the Odin pack.</summary>
public static class OdinRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        OdinLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
