using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.M4;

/// <summary>Registration helpers for the M4 / Autoconf pack.</summary>
public static class M4Registration
{
    /// <summary>Registers the pack and its outline provider.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        M4LanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
