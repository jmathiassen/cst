using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Rust;

/// <summary>Registration helpers for the Rust pack.</summary>
public static class RustRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        RustLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
