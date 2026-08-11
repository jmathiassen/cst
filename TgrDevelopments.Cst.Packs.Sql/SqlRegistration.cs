using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Sql;

/// <summary>Registration helpers for the Sql pack.</summary>
public static class SqlRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        SqlLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
