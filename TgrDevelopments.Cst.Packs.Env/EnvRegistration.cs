using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Env;

/// <summary>Registration helpers for the Env pack.</summary>
public static class EnvRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        EnvLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
