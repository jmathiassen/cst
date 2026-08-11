using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Rst;

/// <summary>Registration helpers for the Rst pack.</summary>
public static class RstRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        RstLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
