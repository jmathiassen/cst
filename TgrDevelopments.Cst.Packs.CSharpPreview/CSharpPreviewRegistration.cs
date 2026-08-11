using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.CSharpPreview;

/// <summary>Registration helpers.</summary>
public static class CSharpPreviewRegistration
{
    /// <summary>Registers pack, outline provider, and optional structural extractor.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        CSharpPreviewLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
        registry.RegisterExtractor(new CSharpPreviewStructuralExtractor());
    }
}
