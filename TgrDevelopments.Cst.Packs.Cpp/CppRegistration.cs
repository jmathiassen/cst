using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Cpp;

/// <summary>Registration helpers for the Cpp pack.</summary>
public static class CppRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        CppLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
