using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Lua;

/// <summary>Registration helpers for the Lua pack.</summary>
public static class LuaRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        LuaLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
