using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Sln;

/// <summary>Registration helpers.</summary>
public static class SlnRegistration
{
    /// <summary>Registers pack, outline provider, and optional structural extractor.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        SlnLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
        registry.RegisterExtractor(new SlnStructuralExtractor());
    }
}
