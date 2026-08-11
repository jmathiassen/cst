using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.JavaScript;

/// <summary>Registration helpers.</summary>
public static class JavaScriptRegistration
{
    /// <summary>Registers pack, outline provider, and structural extractor.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        JavaScriptLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
        registry.RegisterExtractor(new JavaScriptStructuralExtractor());
    }
}
