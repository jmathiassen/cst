using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Php;

/// <summary>Registration helpers for the Php pack.</summary>
public static class PhpRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        PhpLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
