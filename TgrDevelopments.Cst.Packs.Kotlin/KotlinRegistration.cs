using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Kotlin;

/// <summary>Registration helpers for the Kotlin pack.</summary>
public static class KotlinRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        KotlinLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
