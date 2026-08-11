using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.FSharp;

/// <summary>Registration helpers for the FSharp pack.</summary>
public static class FSharpRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        FSharpLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
