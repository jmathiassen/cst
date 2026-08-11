namespace TgrDevelopments.Cst;

/// <summary>
/// Documentation-only placeholder. Core never references language packs (isolation).
/// Hosts register packs via each pack assembly's <c>XxxRegistration.Register</c>:
/// <code>
/// LanguagePackRegistry registry = new();
/// JsonRegistration.Register(registry);
/// MarkdownRegistration.Register(registry);
/// // …or a host-level helper that references pack projects and calls each Register.
/// CstService service = new(registry);
/// </code>
/// Convention: every pack exposes <c>TgrDevelopments.Cst.Packs.{Name}.{Name}Registration.Register</c>
/// in a dedicated <c>{Name}Registration.cs</c> file.
/// </summary>
public static class StandardPacks
{
}
