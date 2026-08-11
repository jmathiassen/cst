using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.PowerShell;

/// <summary>Registration helpers for the PowerShell pack.</summary>
public static class PowerShellRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        PowerShellLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
