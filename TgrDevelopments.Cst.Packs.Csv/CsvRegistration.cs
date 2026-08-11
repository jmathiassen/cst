using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Csv;

/// <summary>Registration helpers for the CSV pack.</summary>
public static class CsvRegistration
{
    /// <summary>Registers the CSV pack and its outline provider.</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        CsvLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
