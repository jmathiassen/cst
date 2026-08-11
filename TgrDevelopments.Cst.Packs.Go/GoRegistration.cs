using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Go;

/// <summary>Registration helpers for the Go pack.</summary>
public static class GoRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        GoLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
