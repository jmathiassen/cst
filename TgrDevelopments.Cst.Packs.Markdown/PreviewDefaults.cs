using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Markdown;

/// <summary>Markdown-only registration helper (no cross-pack references).</summary>
public static class PreviewDefaults
{
    /// <summary>Registers the Markdown pack and outline provider.</summary>
    public static void AddMarkdown(ILanguagePackRegistry registry)
        => MarkdownRegistration.Register(registry);
}
