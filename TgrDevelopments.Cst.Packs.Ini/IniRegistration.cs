using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Ini;

/// <summary>Registration helpers.</summary>
public static class IniRegistration
{
    /// <summary>Registers pack and outline provider.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        IniLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
