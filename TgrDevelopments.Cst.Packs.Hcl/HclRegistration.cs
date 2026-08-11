using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Hcl;

/// <summary>Registration helpers for the Hcl pack.</summary>
public static class HclRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        HclLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
