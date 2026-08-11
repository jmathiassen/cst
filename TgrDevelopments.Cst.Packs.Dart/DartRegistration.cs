using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Dart;

/// <summary>Registration helpers for the Dart pack.</summary>
public static class DartRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        DartLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
