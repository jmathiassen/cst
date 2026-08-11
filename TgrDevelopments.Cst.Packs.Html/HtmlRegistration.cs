using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Html;

/// <summary>Registration helpers.</summary>
public static class HtmlRegistration
{
    /// <summary>Registers pack and outline provider.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        HtmlLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
