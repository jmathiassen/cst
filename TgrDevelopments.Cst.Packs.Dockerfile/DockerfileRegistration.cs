using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Dockerfile;

/// <summary>Registration helpers for the Dockerfile pack.</summary>
public static class DockerfileRegistration
{
    /// <summary>Registers the pack and its outline provider (and extractor when present).</summary>
    public static void Register(ILanguagePackRegistry registry)
    {
        DockerfileLanguagePack pack = new();
        registry.Register(pack);
        registry.RegisterOutline(pack);
    }
}
