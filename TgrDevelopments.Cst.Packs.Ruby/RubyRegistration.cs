using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Ruby;

/// <summary>Registration helpers for the Ruby pack.</summary>
public static class RubyRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        RubyLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
