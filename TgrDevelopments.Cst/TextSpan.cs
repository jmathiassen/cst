namespace TgrDevelopments.Cst;

/// <summary>Half-open UTF-16 span (matches .NET string indexing).</summary>
/// <param name="Start">Start offset inclusive.</param>
/// <param name="Length">Length in UTF-16 code units.</param>
public readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>Gets the exclusive end offset.</summary>
    public int End => Start + Length;
}
