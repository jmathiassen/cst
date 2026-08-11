using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Zig;

/// <summary>Registration helpers for the Zig pack.</summary>
public static class ZigRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        ZigLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
