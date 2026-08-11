using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Shell;

/// <summary>Registration helpers for the Shell pack.</summary>
public static class ShellRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        ShellLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
