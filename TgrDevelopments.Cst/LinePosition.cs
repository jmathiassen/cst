namespace TgrDevelopments.Cst;

/// <summary>1-based line and column (host display).</summary>
/// <param name="Line">1-based line number.</param>
/// <param name="Column">1-based column number.</param>
public readonly record struct LinePosition(int Line, int Column);

/// <summary>Inclusive line/column range for navigation.</summary>
/// <param name="Start">Start position.</param>
/// <param name="End">End position.</param>
public readonly record struct LinePositionSpan(LinePosition Start, LinePosition End);
