namespace TgrDevelopments.Cst;

/// <summary>Options controlling parse behavior.</summary>
public sealed class ParseOptions
{
    /// <summary>Soft cap on input chars; excess → diagnostic + partial (default prefix policy).</summary>
    public int MaxChars { get; init; } = 2_000_000;

    /// <summary>When true, prefer speed over complete recovery.</summary>
    public bool PreferSpeed { get; init; }
}
