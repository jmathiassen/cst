using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Yaml;

/// <summary>Registration helpers.</summary>
public static class YamlRegistration
{
    /// <summary>Registers pack and outline provider.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        YamlLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
