using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Xml;

/// <summary>Registration helpers.</summary>
public static class XmlRegistration
{
    /// <summary>Registers pack, outline provider, and optional structural extractor.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        XmlLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
        registry.RegisterExtractor(new XmlStructuralExtractor());
    }
}
