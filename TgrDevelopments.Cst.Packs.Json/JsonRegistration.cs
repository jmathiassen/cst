using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Json;

/// <summary>Registration helpers for the JSON pack.</summary>
public static class JsonRegistration
{
    /// <summary>Registers the JSON pack and its outline provider.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        JsonLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
