using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.TypeScript;

/// <summary>Registration helpers for the TypeScript pack.</summary>
public static class TypeScriptRegistration
{
    /// <summary>Registers pack, outline provider, and structural extractor.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        TypeScriptLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
        registry.RegisterExtractor(new TypeScriptStructuralExtractor());
    }
}
