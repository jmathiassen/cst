using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Proto;

/// <summary>Registration helpers for the Proto pack.</summary>
public static class ProtoRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        ProtoLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
