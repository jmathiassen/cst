using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Python;

/// <summary>Registration helpers for the Python pack.</summary>
public static class PythonRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        PythonLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
