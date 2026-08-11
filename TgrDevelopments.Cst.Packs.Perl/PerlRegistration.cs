using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Perl;

public static class PerlRegistration
{
    public static void Register(ILanguagePackRegistry registry)
    {
        PerlLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
