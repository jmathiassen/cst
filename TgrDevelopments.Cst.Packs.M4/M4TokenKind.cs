namespace TgrDevelopments.Cst.Packs.M4;

internal enum M4TokenKind
{
    Eof = -1,
    Identifier = 1,
    LParen,
    RParen,
    Comma,
    /// <summary>Balanced <c>[…]</c> quote span (opaque payload).</summary>
    Quoted,
    StringLiteral,
    Other,
}
