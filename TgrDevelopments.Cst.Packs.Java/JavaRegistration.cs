using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Java;

/// <summary>Registration helpers for the Java pack.</summary>
public static class JavaRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        JavaLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
