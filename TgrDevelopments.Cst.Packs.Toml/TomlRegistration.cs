using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Toml;

/// <summary>Registration helpers.</summary>
public static class TomlRegistration
{
    /// <summary>Registers pack and outline provider.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        TomlLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
