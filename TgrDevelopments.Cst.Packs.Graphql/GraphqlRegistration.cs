using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Graphql;

/// <summary>Registration helpers for the Graphql pack.</summary>
public static class GraphqlRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        GraphqlLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
