namespace TgrDevelopments.Cst;

/// <summary>Single lexer token. <see cref="Kind"/> is pack-defined (enum underlying int).</summary>
/// <param name="Kind">Pack token kind (cast from pack enum).</param>
/// <param name="Span">UTF-16 span in source.</param>
/// <param name="Text">Optional captured lexeme (identifiers, numbers, string contents).</param>
public readonly record struct Token(int Kind, TextSpan Span, string? Text = null)
{
    /// <summary>End-exclusive offset.</summary>
    public int End => Span.End;

    /// <summary>Start offset.</summary>
    public int Start => Span.Start;

    /// <summary>True when this is the EOF sentinel.</summary>
    public bool IsEof => Kind < 0;
}
